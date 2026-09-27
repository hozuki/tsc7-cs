// ported from: internal/checker/checker.go:9026-9579（重载决议：resolveCall、reorderCandidates、
//   signatureHasLiteralTypes、getOptionalCallSignature、chooseOverload、hasCorrectArity、acceptsVoid、
//   getDecoratorArgumentCount、getLegacyDecoratorArgumentCount、hasCorrectTypeArgumentArity、checkTypeArguments、
//   isSignatureApplicable、maybeAddMissingAwaitInfo、getThisArgumentOfCall、getThisArgumentType、getEffectiveCheckNode）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) Go 的 `*[]*Signature` / `*[]*ast.Diagnostic`（可读可写的"出参"）→ `ref`：`candidatesOutArray` 在 Go 里被读
//    （判 nil）、`diagnosticOutput` 在 `maybeAddMissingAwaitInfo` 里被读，所以不是 `out`。Go 传 `nil` 的调用点
//    改写成局部变量 + `ref 局部`。`ref` 形参可以直传给别的片声明的 `out` 形参
//    （如 `CheckTypeAssignableToEx(..., out var diags)`），C# 允许这种实参。
// 2) 包级函数 `signatureHasLiteralTypes`/`acceptsVoid` → 同一个 partial 类里的 `internal static`（§5.5）。
// 3) Worker 改名（artifacts/checker-method-collisions.md）：`getTypeOfSymbol` → `GetTypeOfSymbolWorker`、
//    `hasEffectiveRestParameter` → `HasEffectiveRestParameterWorker`、`getTypeAtPosition` → `GetTypeAtPositionWorker`、
//    `getMinTypeArgumentCount` → `GetMinTypeArgumentCountWorker`、`fillMissingTypeArguments` →
//    `FillMissingTypeArgumentsWorker`、`getTypeFromTypeNode` → `GetTypeFromTypeNodeWorker`、
//    `getConstraintOfTypeParameter` → `GetConstraintOfTypeParameterWorker`；`SignatureToString`/`TypeToString`
//    是 Go 导出名，保留。
// 4) Go 的 `s.candidatesForArgumentError = append(s.candidatesForArgumentError, checkCandidate)` →
//    集合表达式 `[.. s.candidatesForArgumentError ?? [], checkCandidate]`（C# 数组不能就地变长；
//    Go 的 append 同样返回新切片）。
// 5) `min`/`max` 内建 → `Math.Min`/`Math.Max`；`args[len(args)-1]` → `args[^1]`。
// 6) `panic(...)` → `Debug.Fail(...)` + `throw new System.Diagnostics.UnreachableException()`。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;

public sealed partial class Checker
{
    internal Signature ResolveCall(Node node, Signature[] signatures, ref Signature[]? candidatesOutArray, CheckMode checkMode, SignatureFlags callChainFlags, Message? headMessage)
    {
        var isTaggedTemplate = node.Kind == Kind.TaggedTemplateExpression;
        var isDecorator = node.Kind == Kind.Decorator;
        var isJsxOpeningOrSelfClosingElement = IsJsxOpeningLikeElement(node);
        var isInstanceof = node.Kind == Kind.BinaryExpression;
        var reportErrors = !_isInferencePartiallyBlocked && candidatesOutArray == null;
        var s = new CallState();
        s._node = node;
        if (!isDecorator && !isInstanceof && !IsSuperCall(node) && !IsJsxOpeningFragment(node))
        {
            s._typeArguments = node.TypeArguments();
            // We already perform checking on the type arguments on the class declaration itself.
            if (isTaggedTemplate || isJsxOpeningOrSelfClosingElement || node.Expression()!.Kind != Kind.SuperKeyword)
            {
                CheckSourceElements(s._typeArguments ?? []);
            }
        }
        s._candidates = ReorderCandidates(signatures, callChainFlags);
        if (candidatesOutArray != null)
        {
            candidatesOutArray = s._candidates;
        }

        if (s._candidates.Length == 0)
        {
            // In Strada we would error here, but no known repro doesn't have at least
            // one other error in this codepath. Just return instead. See #54442
            return _unknownSignature!;
        }

        s._args = GetEffectiveCallArguments(node);
        // The excludeArgument array contains true for each context sensitive argument (an argument
        // is context sensitive it is susceptible to a one-time permanent contextual typing).
        //
        // The idea is that we will perform type argument inference & assignability checking once
        // without using the susceptible parameters that are functions, and once more for those
        // parameters, contextually typing each as we go along.
        //
        // For a tagged template, then the first argument be 'undefined' if necessary because it
        // represents a TemplateStringsArray.
        //
        // For a decorator, no arguments are susceptible to contextual typing due to the fact
        // decorators are applied to a declaration by the emitter, and not to an expression.
        s._isSingleNonGenericCandidate = s._candidates.Length == 1 && s._candidates[0].TypeParameters().Length == 0;
        if (!isDecorator && !s._isSingleNonGenericCandidate && Some(s._args, a => IsContextSensitiveWorker(a)))
        {
            s._argCheckMode = CheckMode.SkipContextSensitive;
        }
        else
        {
            s._argCheckMode = CheckMode.Normal;
        }
        // The following variables are captured and modified by calls to chooseOverload.
        // If overload resolution or type argument inference fails, we want to report the
        // best error possible. The best error is one which says that an argument was not
        // assignable to a parameter. This implies that everything else about the overload
        // was fine. So if there is any overload that is only incorrect because of an
        // argument, we will report an error on that one.
        //
        //     function foo(s: string): void;
        //     function foo(n: number): void; // Report argument error on this overload
        //     function foo(): void;
        //     foo(true);
        //
        // If none of the overloads even made it that far, there are two possibilities.
        // There was a problem with type arguments for some overload, in which case
        // report an error on that. Or none of the overloads even had correct arity,
        // in which case give an arity error.
        //
        //     function foo<T extends string>(x: T): void; // Report type argument error
        //     function foo(): void;
        //     foo<number>(0);
        //
        // If we are in signature help, a trailing comma indicates that we intend to provide another argument,
        // so we will only accept overloads with arity at least 1 higher than the current number of provided arguments.
        s._signatureHelpTrailingComma = (checkMode & CheckMode.IsForSignatureHelp) != 0 && IsCallExpression(node) && node.ArgumentList()!.HasTrailingComma();
        // Section 4.12.1:
        // if the candidate list contains one or more signatures for which the type of each argument
        // expression is a subtype of each corresponding parameter type, the return type of the first
        // of those signatures becomes the return type of the function call.
        // Otherwise, the return type of the first signature in the candidate list becomes the return
        // type of the function call.
        //
        // Whether the call is an error is determined by assignability of the arguments. The subtype pass
        // is just important for choosing the best signature. So in the case where there is only one
        // signature, the subtype pass is useless. So skipping it is an optimization.
        Signature? result = null;
        s._recursiveResolution = _callResolutionStack.Contains(s._node!);
        _callResolutionStack.Add(s._node!);
        if (s._candidates.Length > 1)
        {
            result = ChooseOverload(s, _subtypeRelation!);
        }
        if (result == null)
        {
            result = ChooseOverload(s, _assignableRelation!);
        }
        _callResolutionStack.RemoveAt(_callResolutionStack.Count - 1);
        if (result != null)
        {
            return result;
        }
        result = GetCandidateForOverloadFailure(s._node, s._candidates, s._args, candidatesOutArray != null, checkMode);
        // Preemptively cache the result; getResolvedSignature will do this after we return, but
        // we need to ensure that the result is present for the error checks below so that if
        // this signature is encountered again, we handle the circularity (rather than producing a
        // different result which may produce no errors and assert). Callers of getResolvedSignature
        // don't hit this issue because they only observe this result after it's had a chance to
        // be cached, but the error reporting code below executes before getResolvedSignature sets
        // resolvedSignature.
        _signatureLinks.Get(node)._resolvedSignature = result;
        // No signatures were applicable. Now report errors based on the last applicable signature with
        // no arguments excluded from assignability checks.
        // If candidate is undefined, it means that no candidates had a suitable arity. In that case,
        // skip the checkApplicableSignature check.
        if (reportErrors)
        {
            // If the call expression is a synthetic call to a `[Symbol.hasInstance]` method then we will produce a head
            // message when reporting diagnostics that explains how we got to `right[Symbol.hasInstance](left)` from
            // `left instanceof right`, as it pertains to "Argument" related messages reported for the call.
            if (headMessage == null && isInstanceof)
            {
                headMessage = Diagnostics.The_left_hand_side_of_an_instanceof_expression_must_be_assignable_to_the_first_argument_of_the_right_hand_side_s_Symbol_hasInstance_method;
            }
            ReportCallResolutionErrors(node, s, signatures, headMessage);
        }
        return result;
    }

    internal Signature[] ReorderCandidates(Signature[] signatures, SignatureFlags callChainFlags)
    {
        Node? lastParent = null;
        Symbol? lastSymbol = null;
        var index = 0;
        var cutoffIndex = 0;
        var spliceIndex = 0;
        var specializedIndex = -1;
        var result = new List<Signature>(signatures.Length);
        foreach (var initialSignature in signatures)
        {
            var signature = initialSignature;
            Symbol? symbol = null;
            Node? parent = null;
            if (signature._declaration != null)
            {
                symbol = GetSymbolOfDeclaration(signature._declaration);
                parent = signature._declaration.Parent;
            }
            if (lastSymbol == null || symbol == lastSymbol)
            {
                if (lastParent != null && parent == lastParent)
                {
                    index = index + 1;
                }
                else
                {
                    lastParent = parent;
                    index = cutoffIndex;
                }
            }
            else
            {
                // current declaration belongs to a different symbol
                // set cutoffIndex so re-orderings in the future won't change result set from 0 to cutoffIndex
                index = result.Count;
                cutoffIndex = result.Count;
                lastParent = parent;
            }
            lastSymbol = symbol;
            // specialized signatures always need to be placed before non-specialized signatures regardless
            // of the cutoff position; see GH#1133
            if (SignatureHasLiteralTypes(signature))
            {
                specializedIndex++;
                spliceIndex = specializedIndex;
                // The cutoff index always needs to be greater than or equal to the specialized signature index
                // in order to prevent non-specialized signatures from being added before a specialized
                // signature.
                cutoffIndex++;
            }
            else
            {
                spliceIndex = index;
            }
            if (callChainFlags != 0)
            {
                signature = GetOptionalCallSignature(signature, callChainFlags);
            }
            result.Insert(spliceIndex, signature);
        }
        return result.ToArray();
    }

    internal static bool SignatureHasLiteralTypes(Signature s)
    {
        return (s._flags & SignatureFlags.HasLiteralTypes) != 0;
    }

    internal Signature GetOptionalCallSignature(Signature signature, SignatureFlags callChainFlags)
    {
        if ((signature._flags & SignatureFlags.CallChainFlags) == callChainFlags)
        {
            return signature;
        }
        var key = new CachedSignatureKey
        {
            _sig = signature,
            _key = IfElse(callChainFlags == SignatureFlags.IsInnerCallChain, SignatureKeyInner, SignatureKeyOuter),
        };
        if (_cachedSignatures.TryGetValue(key, out var cached) && cached != null)
        {
            return cached;
        }
        var result = CloneSignature(signature);
        result._flags |= callChainFlags;
        _cachedSignatures[key] = result;
        return result;
    }

    internal Signature? ChooseOverload(CallState s, Relation relation)
    {
        s._candidatesForArgumentError = null;
        s._candidateForArgumentArityError = null;
        s._candidateForTypeArgumentError = null;
        if (s._isSingleNonGenericCandidate)
        {
            var candidate = s._candidates[0];
            if ((s._typeArguments != null && s._typeArguments.Length != 0) || !HasCorrectArity(s._node!, s._args, candidate, s._signatureHelpTrailingComma))
            {
                return null;
            }
            Diagnostic[]? noDiags = null;
            if (!IsSignatureApplicable(s._node!, s._args, candidate, relation, CheckMode.Normal, false /*reportErrors*/, ref noDiags /*diagnosticOutput*/))
            {
                s._candidatesForArgumentError = [candidate];
                return null;
            }
            return candidate;
        }
        for (var candidateIndex = 0; candidateIndex < s._candidates.Length; candidateIndex++)
        {
            var candidate = s._candidates[candidateIndex];
            if (!HasCorrectTypeArgumentArity(candidate, s._typeArguments ?? []) || !HasCorrectArity(s._node!, s._args, candidate, s._signatureHelpTrailingComma))
            {
                continue;
            }
            Signature checkCandidate;
            InferenceContext? inferenceContext = null;
            if (candidate.TypeParameters().Length != 0)
            {
                Type[]? typeArgumentTypes;
                if (s._typeArguments != null && s._typeArguments.Length != 0)
                {
                    typeArgumentTypes = CheckTypeArguments(candidate, s._typeArguments, false /*reportErrors*/, null);
                    if (typeArgumentTypes == null)
                    {
                        s._candidateForTypeArgumentError = candidate;
                        continue;
                    }
                }
                else
                {
                    // When we are recursively resolving a call with a single candidate, we skip constraints checks during
                    // type inference to avoid circularity errors. For example, see #64192.
                    var inferenceFlags = IfElse(s._recursiveResolution && s._candidates.Length == 1, InferenceFlags.NoConstraintChecks, InferenceFlags.None) |
                        IfElse(IsInJSFile(s._node!), InferenceFlags.AnyDefault, InferenceFlags.None);
                    inferenceContext = NewInferenceContext(candidate._typeParameters, candidate, inferenceFlags /*flags*/, null);
                    typeArgumentTypes = InferTypeArguments(s._node!, candidate, s._args, s._argCheckMode | CheckMode.SkipGenericFunctions, inferenceContext);
                    if ((inferenceContext._flags & InferenceFlags.SkippedGenericFunction) != 0)
                    {
                        s._argCheckMode |= CheckMode.SkipGenericFunctions;
                    }
                }
                Type[]? inferredTypeParameters = null;
                if (inferenceContext != null)
                {
                    inferredTypeParameters = [.. inferenceContext._inferredTypeParameters];
                }
                checkCandidate = GetSignatureInstantiation(candidate, typeArgumentTypes, IsInJSFile(candidate._declaration), inferredTypeParameters);
                // If the original signature has a generic rest type, instantiation may produce a
                // signature with different arity and we need to perform another arity check.
                if (GetNonArrayRestType(candidate) != null && !HasCorrectArity(s._node!, s._args, checkCandidate, s._signatureHelpTrailingComma))
                {
                    s._candidateForArgumentArityError = checkCandidate;
                    continue;
                }
            }
            else
            {
                checkCandidate = candidate;
            }
            Diagnostic[]? checkDiags = null;
            if (!IsSignatureApplicable(s._node!, s._args, checkCandidate, relation, s._argCheckMode, false /*reportErrors*/, ref checkDiags /*diagnosticOutput*/))
            {
                // Give preference to error candidates that have no rest parameters (as they are more specific)
                s._candidatesForArgumentError = [.. s._candidatesForArgumentError ?? [], checkCandidate];
                continue;
            }
            if (s._argCheckMode != 0)
            {
                // If one or more context sensitive arguments were excluded, we start including
                // them now (and keeping do so for any subsequent candidates) and perform a second
                // round of type inference and applicability checking for this particular candidate.
                s._argCheckMode = CheckMode.Normal;
                if (inferenceContext != null)
                {
                    var typeArgumentTypes = InferTypeArguments(s._node!, candidate, s._args, s._argCheckMode, inferenceContext);
                    checkCandidate = GetSignatureInstantiation(candidate, typeArgumentTypes, IsInJSFile(candidate._declaration), [.. inferenceContext._inferredTypeParameters]);
                    // If the original signature has a generic rest type, instantiation may produce a
                    // signature with different arity and we need to perform another arity check.
                    if (GetNonArrayRestType(candidate) != null && !HasCorrectArity(s._node!, s._args, checkCandidate, s._signatureHelpTrailingComma))
                    {
                        s._candidateForArgumentArityError = checkCandidate;
                        continue;
                    }
                }
                Diagnostic[]? checkDiags2 = null;
                if (!IsSignatureApplicable(s._node!, s._args, checkCandidate, relation, s._argCheckMode, false /*reportErrors*/, ref checkDiags2 /*diagnosticOutput*/))
                {
                    // Give preference to error candidates that have no rest parameters (as they are more specific)
                    s._candidatesForArgumentError = [.. s._candidatesForArgumentError ?? [], checkCandidate];
                    continue;
                }
            }
            s._candidates[candidateIndex] = checkCandidate;
            return checkCandidate;
        }
        return null;
    }

    internal bool HasCorrectArity(Node node, Node[] args, Signature signature, bool signatureHelpTrailingComma)
    {
        if (IsJsxOpeningFragment(node))
        {
            return true;
        }
        int argCount;
        var callIsIncomplete = false;
        // In incomplete call we want to be lenient when we have too few arguments
        var effectiveParameterCount = GetParameterCount(signature);
        var effectiveMinimumArguments = GetMinArgumentCount(signature);
        if (IsTaggedTemplateExpression(node))
        {
            argCount = args.Length;
            var template = node.AsTaggedTemplateExpression()!.Template!;
            if (IsTemplateExpression(template))
            {
                // If a tagged template expression lacks a tail literal, the call is incomplete.
                // Specifically, a template only can end in a TemplateTail or a Missing literal.
                var lastSpan = LastOrNil(template.AsTemplateExpression()!.TemplateSpans?.Nodes);
                // we should always have at least one span.
                callIsIncomplete = NodeIsMissing(lastSpan!.AsTemplateSpan()!.Literal) || IsUnterminatedLiteral(lastSpan.AsTemplateSpan()!.Literal!);
            }
            else
            {
                // If the template didn't end in a backtick, or its beginning occurred right prior to EOF,
                // then this might actually turn out to be a TemplateHead in the future;
                // so we consider the call to be incomplete.
                callIsIncomplete = IsUnterminatedLiteral(template);
            }
        }
        else if (IsDecorator(node))
        {
            argCount = GetDecoratorArgumentCount(node, signature);
        }
        else if (IsBinaryExpression(node))
        {
            argCount = 1;
        }
        else if (IsJsxOpeningLikeElement(node))
        {
            callIsIncomplete = node.Attributes()!.End() == node.End();
            if (callIsIncomplete)
            {
                return true;
            }
            argCount = IfElse(effectiveMinimumArguments == 0, args.Length, 1);
            effectiveParameterCount = IfElse(args.Length == 0, effectiveParameterCount, 1); // class may have argumentless ctor functions - still resolve ctor and compare vs props member type
            effectiveMinimumArguments = Math.Min(effectiveMinimumArguments, 1);            // sfc may specify context argument - handled by framework and not typechecked
        }
        else if (IsNewExpression(node) && node.ArgumentList() == null)
        {
            // This only happens when we have something of the form: 'new C'
            return GetMinArgumentCount(signature) == 0;
        }
        else
        {
            argCount = signatureHelpTrailingComma ? args.Length + 1 : args.Length;
            // If we are missing the close parenthesis, the call is incomplete.
            callIsIncomplete = node.ArgumentList()!.End() == node.End();
            // If a spread argument is present, check that it corresponds to a rest parameter or at least that it's in the valid range.
            var spreadArgIndex = GetSpreadArgumentIndex(args);
            if (spreadArgIndex >= 0)
            {
                return spreadArgIndex >= GetMinArgumentCount(signature) && (HasEffectiveRestParameterWorker(signature) || spreadArgIndex < GetParameterCount(signature));
            }
        }
        // Too many arguments implies incorrect arity.
        if (!HasEffectiveRestParameterWorker(signature) && argCount > effectiveParameterCount)
        {
            return false;
        }
        // If the call is incomplete, we should skip the lower bound check.
        // JSX signatures can have extra parameters provided by the library which we don't check
        if (callIsIncomplete || argCount >= effectiveMinimumArguments)
        {
            return true;
        }
        for (var i = argCount; i < effectiveMinimumArguments; i++)
        {
            var t = GetTypeAtPositionWorker(signature, i);
            if ((FilterType(t, AcceptsVoid)._flags & TypeFlags.Never) != 0)
            {
                return false;
            }
        }
        return true;
    }

    internal static bool AcceptsVoid(Type t)
    {
        return (t._flags & TypeFlags.Void) != 0;
    }

    internal int GetDecoratorArgumentCount(Node node, Signature signature)
    {
        if (_compilerOptions.ExperimentalDecorators.IsTrue())
        {
            return GetLegacyDecoratorArgumentCount(node, signature);
        }
        return Math.Min(Math.Max(GetParameterCount(signature), 1), 2);
    }

    /**
     * Returns the argument count for a decorator node that works like a function invocation.
     */
    internal int GetLegacyDecoratorArgumentCount(Node node, Signature signature)
    {
        switch (node.Parent!.Kind)
        {
            case Kind.ClassDeclaration:
            case Kind.ClassExpression:
                return 1;
            case Kind.PropertyDeclaration:
                if (HasAccessorModifier(node.Parent))
                {
                    return 3;
                }
                return 2;
            case Kind.MethodDeclaration:
            case Kind.GetAccessor:
            case Kind.SetAccessor:
                // For decorators with only two parameters we supply only two arguments
                if (GetParameterCount(signature) <= 2)
                {
                    return 2;
                }
                return 3;
            case Kind.Parameter:
                return 3;
        }
        Debug.Fail("Unhandled case in getLegacyDecoratorArgumentCount");
        throw new System.Diagnostics.UnreachableException();
    }

    internal bool HasCorrectTypeArgumentArity(Signature signature, Node[] typeArguments)
    {
        // If the user supplied type arguments, but the number of type arguments does not match
        // the declared number of type parameters, the call has an incorrect arity.
        var numTypeParameters = signature.TypeParameters().Length;
        var minTypeArgumentCount = GetMinTypeArgumentCountWorker(signature._typeParameters);
        return typeArguments.Length == 0 || typeArguments.Length >= minTypeArgumentCount && typeArguments.Length <= numTypeParameters;
    }

    internal Type[]? CheckTypeArguments(Signature signature, Node[]? typeArgumentNodes, bool reportErrors, Message? headMessage)
    {
        var isJavaScript = IsInJSFile(signature._declaration);
        var typeParameters = signature.TypeParameters();
        var argumentNodes = typeArgumentNodes ?? [];
        var typeArgumentTypes = FillMissingTypeArgumentsWorker(Map(argumentNodes, n => GetTypeFromTypeNodeWorker(n)), typeParameters, GetMinTypeArgumentCountWorker(typeParameters), isJavaScript) ?? [];
        TypeMapper? mapper = null;
        for (var i = 0; i < argumentNodes.Length; i++)
        {
            Debug.Assert(typeParameters[i] != null, "Should not call checkTypeArguments with too many type arguments");
            var constraint = GetConstraintOfTypeParameterWorker(typeParameters[i]);
            if (constraint != null)
            {
                var typeArgumentHeadMessage = OrElse(headMessage, Diagnostics.Type_0_does_not_satisfy_the_constraint_1);
                if (mapper == null)
                {
                    mapper = NewTypeMapper(typeParameters, typeArgumentTypes);
                }
                var typeArgument = typeArgumentTypes[i];
                Node? errorNode = null;
                if (reportErrors)
                {
                    errorNode = argumentNodes[i];
                }
                if (!CheckTypeAssignableToEx(typeArgument, GetTypeWithThisArgument(InstantiateType(constraint, mapper), typeArgument, false), errorNode, typeArgumentHeadMessage, out var diags))
                {
                    if (diags != null && diags.Length != 0)
                    {
                        var diagnostic = diags[0];
                        if (headMessage != null)
                        {
                            diagnostic = NewDiagnosticChain(diagnostic, Diagnostics.Type_0_does_not_satisfy_the_constraint_1)!;
                        }
                        AddDiagnostic(diagnostic);
                    }
                    return null;
                }
            }
        }
        return typeArgumentTypes;
    }

    internal bool IsSignatureApplicable(Node node, Node[] args, Signature signature, Relation relation, CheckMode checkMode, bool reportErrors, ref Diagnostic[]? diagnosticOutput)
    {
        if (IsJsxCallLike(node))
        {
            return CheckApplicableSignatureForJsxCallLikeElement(node, signature, relation, checkMode, reportErrors, ref diagnosticOutput);
        }
        var thisType = GetThisTypeOfSignature(signature);
        if (thisType != null && thisType != _voidType && !(IsNewExpression(node) || IsCallExpression(node) && IsSuperProperty(node.Expression()!)))
        {
            // If the called expression is not of the form `x.f` or `x["f"]`, then sourceType = voidType
            // If the signature's 'this' type is voidType, then the check is skipped -- anything is compatible.
            // If the expression is a new expression or super call expression, then the check is skipped.
            var thisArgumentNode = GetThisArgumentOfCall(node);
            var thisArgumentType = GetThisArgumentType(thisArgumentNode);
            Node? errorNode = null;
            if (reportErrors)
            {
                errorNode = thisArgumentNode;
                if (errorNode == null)
                {
                    errorNode = node;
                }
            }
            var thisHeadMessage = Diagnostics.The_this_context_of_type_0_is_not_assignable_to_method_s_this_of_type_1;
            if (!CheckTypeRelatedToEx(thisArgumentType, thisType, relation, errorNode, thisHeadMessage, ref diagnosticOutput))
            {
                return false;
            }
        }
        var headMessage = Diagnostics.Argument_of_type_0_is_not_assignable_to_parameter_of_type_1;
        var restType = GetNonArrayRestType(signature);
        int argCount;
        if (restType != null)
        {
            argCount = Math.Min(GetParameterCount(signature) - 1, args.Length);
        }
        else
        {
            argCount = args.Length;
        }
        for (var i = 0; i < argCount; i++)
        {
            var arg = args[i];
            if (!IsOmittedExpression(arg))
            {
                var paramType = GetTypeAtPositionWorker(signature, i);
                var argType = CheckExpressionWithContextualType(arg, paramType, null /*inferenceContext*/, checkMode);
                // If one or more arguments are still excluded (as indicated by CheckMode.SkipContextSensitive),
                // we obtain the regular type of any object literal arguments because we may not have inferred complete
                // parameter types yet and therefore excess property checks may yield false positives (see #17041).
                Type checkArgType;
                if ((checkMode & CheckMode.SkipContextSensitive) != 0)
                {
                    checkArgType = GetRegularTypeOfObjectLiteral(argType);
                }
                else
                {
                    checkArgType = argType;
                }
                var effectiveCheckArgumentNode = GetEffectiveCheckNode(arg);
                if (!CheckTypeRelatedToAndOptionallyElaborate(checkArgType, paramType, relation, IfElse(reportErrors, effectiveCheckArgumentNode, null), effectiveCheckArgumentNode, headMessage, ref diagnosticOutput))
                {
                    MaybeAddMissingAwaitInfo(arg, checkArgType, paramType, relation, reportErrors, ref diagnosticOutput);
                    return false;
                }
            }
        }
        if (restType != null)
        {
            var spreadType = GetSpreadArgumentType(args, argCount, args.Length, restType, null /*context*/, checkMode);
            var restArgCount = args.Length - argCount;
            Node? errorNode = null;
            if (reportErrors)
            {
                if (restArgCount == 0)
                {
                    errorNode = node;
                }
                else if (restArgCount == 1)
                {
                    errorNode = GetEffectiveCheckNode(args[argCount]);
                }
                else
                {
                    errorNode = CreateSyntheticExpression(node, spreadType, false, null)!;
                    errorNode.Loc = NewTextRange(args[argCount].Pos(), args[^1].End());
                }
            }
            if (!CheckTypeRelatedToEx(spreadType, restType, relation, errorNode, headMessage, ref diagnosticOutput))
            {
                MaybeAddMissingAwaitInfo(errorNode, spreadType, restType, relation, reportErrors, ref diagnosticOutput);
                return false;
            }
        }
        return true;
    }

    internal void MaybeAddMissingAwaitInfo(Node? errorNode, Type source, Type target, Relation relation, bool reportErrors, ref Diagnostic[]? diagnosticOutput)
    {
        if (errorNode != null && reportErrors && diagnosticOutput != null && diagnosticOutput.Length != 0)
        {
            // Bail if target is Promise-like---something else is wrong
            if (GetAwaitedTypeOfPromise(target) != null)
            {
                return;
            }
            var awaitedTypeOfSource = GetAwaitedTypeOfPromise(source);
            if (awaitedTypeOfSource != null && IsTypeRelatedTo(awaitedTypeOfSource, target, relation))
            {
                diagnosticOutput[0].AddRelatedInfo(NewDiagnosticForNode(errorNode, Diagnostics.Did_you_forget_to_use_await));
            }
        }
    }

    // Returns the `this` argument node in calls like `x.f(...)` and `x[f](...)`. `nil` otherwise.
    internal Node? GetThisArgumentOfCall(Node node)
    {
        if (IsBinaryExpression(node))
        {
            return node.AsBinaryExpression()!.Right;
        }
        Node? expression = null;
        if (IsCallExpression(node))
        {
            expression = node.Expression();
        }
        else if (IsTaggedTemplateExpression(node))
        {
            expression = node.AsTaggedTemplateExpression()!.Tag;
        }
        else if (IsDecorator(node) && !_legacyDecorators)
        {
            expression = node.Expression();
        }
        if (expression != null)
        {
            var callee = SkipOuterExpressions(expression, OuterExpressionKinds.All);
            if (IsAccessExpression(callee!))
            {
                return callee!.Expression();
            }
        }
        return null;
    }

    internal Type GetThisArgumentType(Node? node)
    {
        if (node == null)
        {
            return _voidType!;
        }
        var thisArgumentType = CheckExpression(node);
        if (IsOptionalChainRoot(node.Parent!))
        {
            return GetNonNullableType(thisArgumentType);
        }
        if (IsOptionalChain(node.Parent!))
        {
            return RemoveOptionalTypeMarker(thisArgumentType);
        }
        return thisArgumentType;
    }

    internal Node GetEffectiveCheckNode(Node argument)
    {
        var flags = IfElse(
            IsInJSFile(argument),
            OuterExpressionKinds.Parentheses | OuterExpressionKinds.Satisfies | OuterExpressionKinds.ExcludeJSDocTypeAssertion,
            OuterExpressionKinds.Parentheses | OuterExpressionKinds.Satisfies);
        return SkipOuterExpressions(argument, flags)!;
    }
}
