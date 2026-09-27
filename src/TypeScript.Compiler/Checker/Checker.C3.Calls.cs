// ported from: internal/checker/checker.go:8494-9010（调用/新建/标签模板/装饰器/instanceof 的签名解析：
//   checkCallExpression、checkDeprecatedSignature、addDeprecatedSuggestionWithSignature、isSymbolOrSymbolForCall、
//   getResolvedSignature（→ GetResolvedSignatureWorker）、resolveSignature、resolveCallExpression、resolveNewExpression、
//   constructorAccessibilityError（→ ConstructorAccessibilityError）、getConstructorAccessibilityError、
//   typeHasProtectedAccessibleBase、someSignature（→ SomeSignature）、resolveTaggedTemplateExpression、
//   resolveDecorator、isPotentiallyUncalledDecorator、getDiagnosticHeadMessageForDecoratorResolution、
//   resolveInstanceofExpression）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) Go 的 `candidatesOutArray *[]*Signature`（"指向切片的指针"，调用点可读可写回）→ `ref Signature[]? candidatesOutArray`：
//    既能读（Go 判 `candidatesOutArray != nil`）也能写回（`*candidatesOutArray = …`）。Go 传 `nil` 的调用点
//    改写成局部变量 + `ref 局部`（C# 不允许 `ref null`）。
// 2) 包级函数 `someSignature` → 同一个 partial 类里的 `internal static SomeSignature`（§5.5）。
// 3) Worker 改名（artifacts/checker-method-collisions.md）：`getResolvedSignature` → `GetResolvedSignatureWorker`。
// 4) Go 的方法表达式 `(*Checker).reportCannotInvokePossiblyNullOrUndefinedError` → lambda（C# 没有方法表达式）。
// 5) `panic("Unhandled case in …")` → `Debug.Fail(...)` + `throw new System.Diagnostics.UnreachableException()`
//    （Debug.Fail 没有 `[DoesNotReturn]`，其后必须补 throw 才能满足返回值要求）。
// 6) `for _, x := range node.Arguments()`（可能 nil）→ `foreach (var x in node.Arguments() ?? [])`。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Scanner.Scanner;
using static TypeScript.StringUtil.StringUtil;

public sealed partial class Checker
{
    /**
     * Syntactically and semantically checks a call or new expression.
     * @param node The call/new expression to be checked.
     * @returns On success, the expression's signature's return type. On failure, anyType.
     */
    internal Type CheckCallExpression(Node node, CheckMode checkMode)
    {
        CheckGrammarTypeArguments(node, node.TypeArgumentList());
        Signature[]? candidatesOutArray = null;
        var signature = GetResolvedSignatureWorker(node, ref candidatesOutArray, checkMode);
        if (signature == _resolvingSignature)
        {
            // CheckMode.SkipGenericFunctions is enabled and this is a call to a generic function that
            // returns a function type. We defer checking and return silentNeverType.
            return _silentNeverType!;
        }
        CheckDeprecatedSignature(signature, node);
        if (node.Expression()!.Kind == Kind.SuperKeyword)
        {
            return _voidType!;
        }
        if (IsNewExpression(node))
        {
            var declaration = signature._declaration;
            if (declaration != null && !IsConstructorDeclaration(declaration) && !IsConstructSignatureDeclaration(declaration) && !IsConstructorTypeNode(declaration))
            {
                // When resolved signature is a call signature (and not a construct signature) the result type is any
                if (_noImplicitAny)
                {
                    Error(node, Diagnostics.X_new_expression_whose_target_lacks_a_construct_signature_implicitly_has_an_any_type);
                }
                return _anyType!;
            }
        }
        if (IsInJSFile(node) && IsCommonJSRequire(node))
        {
            return ResolveExternalModuleTypeByLiteral(node.Arguments()![0]);
        }
        var returnType = GetReturnTypeOfSignatureWorker(signature);
        // Treat any call to the global 'Symbol' function that is part of a const variable or readonly property
        // as a fresh unique symbol literal type.
        if ((returnType._flags & TypeFlags.ESSymbolLike) != 0 && IsSymbolOrSymbolForCall(node))
        {
            return GetESSymbolLikeTypeForNode(WalkUpParenthesizedExpressions(node.Parent)!);
        }
        if (IsCallExpression(node) && node.QuestionDotToken() == null && IsExpressionStatement(node.Parent!) && (returnType._flags & TypeFlags.Void) != 0 && GetTypePredicateOfSignatureWorker(signature) != null)
        {
            if (!IsDottedName(node.Expression()!))
            {
                Error(node.Expression(), Diagnostics.Assertions_require_the_call_target_to_be_an_identifier_or_qualified_name);
            }
            else if (GetEffectsSignature(node) == null)
            {
                var diagnostic = Error(node.Expression(), Diagnostics.Assertions_require_every_name_in_the_call_target_to_be_declared_with_an_explicit_type_annotation);
                GetTypeOfDottedName(node.Expression()!, diagnostic);
            }
        }
        return returnType;
    }

    internal void CheckDeprecatedSignature(Signature sig, Node node)
    {
        if ((sig._flags & SignatureFlags.IsSignatureCandidateForOverloadFailure) != 0)
        {
            return;
        }
        if (sig._declaration != null && IsDeprecatedDeclaration(sig._declaration))
        {
            var suggestionNode = GetDeprecatedSuggestionNode(node);
            var name = TryGetPropertyAccessOrIdentifierToString(GetInvokedExpression(node)!);
            AddDeprecatedSuggestionWithSignature(suggestionNode, sig._declaration, name, SignatureToString(sig));
        }
    }

    internal Diagnostic AddDeprecatedSuggestionWithSignature(Node location, Node declaration, string deprecatedEntity, string signatureString)
    {
        var message = IfElse(deprecatedEntity != "", Diagnostics.The_signature_0_of_1_is_deprecated, Diagnostics.X_0_is_deprecated);
        var diagnostic = NewDiagnosticForNode(location, message, signatureString, deprecatedEntity);
        return AddDeprecatedSuggestionWorker([declaration], diagnostic);
    }

    internal bool IsSymbolOrSymbolForCall(Node node)
    {
        if (!IsCallExpression(node))
        {
            return false;
        }
        var left = node.Expression()!;
        if (IsPropertyAccessExpression(left) && left.Name()!.Text() == "for")
        {
            left = left.Expression()!;
        }
        if (!IsIdentifier(left) || left.Text() != "Symbol")
        {
            return false;
        }
        // make sure `Symbol` is the global symbol
        var globalESSymbol = _getGlobalESSymbolConstructorSymbolOrNil();
        if (globalESSymbol == null)
        {
            return false;
        }
        return globalESSymbol == _resolveName(left, "Symbol", SymbolFlags.Value, null /*nameNotFoundMessage*/, false /*isUse*/, false);
    }

    /**
     * Resolve a signature of a given call-like expression.
     * @param node a call-like expression to try resolve a signature for
     * @param candidatesOutArray an array of signature to be filled in by the function. It is passed by signature help in the language service;
     *    the function will fill it up with appropriate candidate signatures
     * @return a signature of the call-like expression or undefined if one can't be found
     */
    /// <summary>
    /// Go: <c>getResolvedSignature(node, nil, checkMode)</c> —— 调用者不关心候选数组。
    /// Go 的 <c>*[]*ast.Signature</c> 允许 nil 指针（被调方对 nil 有判空分支），C# 的 <c>ref</c> 接不了
    /// <c>null</c> 字面量，所以补一个按值传的重载：<c>Exports.cs:163</c>、<c>Checker.C4.Semantics.cs:304</c>
    /// 等按 Go 原样写 <c>null</c> 的调用点走这里。
    /// </summary>
    internal Signature GetResolvedSignatureWorker(Node node, Signature[]? candidatesOutArray, CheckMode checkMode)
    {
        return GetResolvedSignatureWorker(node, ref candidatesOutArray, checkMode);
    }

    internal Signature GetResolvedSignatureWorker(Node node, ref Signature[]? candidatesOutArray, CheckMode checkMode)
    {
        var links = _signatureLinks.Get(node);
        // If getResolvedSignature has already been called, we will have cached the resolvedSignature.
        // However, it is possible that either candidatesOutArray was not passed in the first time,
        // or that a different candidatesOutArray was passed in. Therefore, we need to redo the work
        // to correctly fill the candidatesOutArray.
        var cached = links._resolvedSignature;
        if (cached != null && cached != _resolvingSignature && candidatesOutArray == null)
        {
            return cached;
        }
        var saveResolutionStart = _resolutionStart;
        if (cached == null)
        {
            // If we haven't already done so, temporarily reset the resolution stack. This allows us to
            // handle "inverted" situations where, for example, an API client asks for the type of a symbol
            // containined in a function call argument whose contextual type depends on the symbol itself
            // through resolution of the containing function call. By resetting the resolution stack we'll
            // retry the symbol type resolution with the resolvingSignature marker in place to suppress
            // the contextual type circularity.
            _resolutionStart = _typeResolutions.Count;
        }
        links._resolvedSignature = _resolvingSignature;
        var result = ResolveSignature(node, ref candidatesOutArray, checkMode);
        _resolutionStart = saveResolutionStart;
        // When CheckMode.SkipGenericFunctions is set we use resolvingSignature to indicate that call
        // resolution should be deferred.
        if (result != _resolvingSignature)
        {
            // if the signature resolution originated on a node that itself depends on the contextual type
            // then it's possible that the resolved signature might not be the same as the one that would be computed in source order
            // since resolving such signature leads to resolving the potential outer signature, its arguments and thus the very same signature
            // it's possible that this inner resolution sets the resolvedSignature first.
            // In such a case we ignore the local result and reuse the correct one that was cached.
            if (links._resolvedSignature != _resolvingSignature)
            {
                result = links._resolvedSignature!;
            }
            // If signature resolution originated in control flow type analysis (for example to compute the
            // assigned type in a flow assignment) we don't cache the result as it may be based on temporary
            // types from the control flow analysis.
            if (_flowLoopStack.Count == 0)
            {
                links._resolvedSignature = result;
            }
            else
            {
                links._resolvedSignature = cached;
            }
        }
        return result;
    }

    internal Signature ResolveSignature(Node node, ref Signature[]? candidatesOutArray, CheckMode checkMode)
    {
        switch (node.Kind)
        {
            case Kind.CallExpression:
                return ResolveCallExpression(node, ref candidatesOutArray, checkMode);
            case Kind.NewExpression:
                return ResolveNewExpression(node, ref candidatesOutArray, checkMode);
            case Kind.TaggedTemplateExpression:
                return ResolveTaggedTemplateExpression(node, ref candidatesOutArray, checkMode);
            case Kind.Decorator:
                return ResolveDecorator(node, ref candidatesOutArray, checkMode);
            case Kind.JsxOpeningFragment:
            case Kind.JsxOpeningElement:
            case Kind.JsxSelfClosingElement:
                return ResolveJsxOpeningLikeElement(node, ref candidatesOutArray, checkMode);
            case Kind.BinaryExpression:
                return ResolveInstanceofExpression(node, ref candidatesOutArray, checkMode);
        }
        Debug.Fail("Unhandled case in resolveSignature");
        throw new System.Diagnostics.UnreachableException();
    }

    internal Signature ResolveCallExpression(Node node, ref Signature[]? candidatesOutArray, CheckMode checkMode)
    {
        if (node.Expression()!.Kind == Kind.SuperKeyword)
        {
            var superType = CheckSuperExpression(node.Expression()!);
            if (IsTypeAny(superType))
            {
                foreach (var arg in node.Arguments() ?? [])
                {
                    // Still visit arguments so they get marked for visibility, etc
                    CheckExpression(arg);
                }
                return _anySignature!;
            }
            if (!IsErrorType(superType))
            {
                // In super call, the candidate signatures are the matching arity signatures of the base constructor function instantiated
                // with the type arguments specified in the extends clause.
                var baseTypeNode = GetClassExtendsHeritageElement(GetContainingClass(node));
                if (baseTypeNode != null)
                {
                    var baseConstructors = GetInstantiatedConstructorsForTypeArguments(superType, baseTypeNode.TypeArguments(), baseTypeNode);
                    return ResolveCall(node, baseConstructors, ref candidatesOutArray, checkMode, SignatureFlags.None, null);
                }
            }
            return ResolveUntypedCall(node);
        }
        if (IsImportCall(node))
        {
            return ResolveUntypedCall(node);
        }
        SignatureFlags callChainFlags;
        var funcType = CheckExpression(node.Expression()!);
        if (IsCallChain(node))
        {
            var nonOptionalType = GetOptionalExpressionType(funcType, node.Expression()!);
            if (nonOptionalType == funcType)
            {
                callChainFlags = SignatureFlags.None;
            }
            else if (IsOutermostOptionalChain(node))
            {
                callChainFlags = SignatureFlags.IsOuterCallChain;
            }
            else
            {
                callChainFlags = SignatureFlags.IsInnerCallChain;
            }
            funcType = nonOptionalType;
        }
        else
        {
            callChainFlags = SignatureFlags.None;
        }
        funcType = CheckNonNullTypeWithReporter(funcType, node.Expression()!, (c, n, facts) => c.ReportCannotInvokePossiblyNullOrUndefinedError(n, facts));
        if (funcType == _silentNeverType)
        {
            return _silentNeverSignature!;
        }
        var apparentType = GetApparentTypeWorker(funcType);
        if (IsErrorType(apparentType))
        {
            // Another error has already been reported
            return ResolveErrorCall(node);
        }
        // Technically, this signatures list may be incomplete. We are taking the apparent type,
        // but we are not including call signatures that may have been added to the Object or
        // Function interface, since they have none by default. This is a bit of a leap of faith
        // that the user will not add any.
        var callSignatures = GetSignaturesOfTypeWorker(apparentType, SignatureKind.Call) ?? [];
        var numConstructSignatures = GetSignaturesOfTypeWorker(apparentType, SignatureKind.Construct)?.Length ?? 0;
        // TS 1.0 Spec: 4.12
        // In an untyped function call no TypeArgs are permitted, Args can be any argument list, no contextual
        // types are provided for the argument expressions, and the result is always of type Any.
        if (IsUntypedFunctionCall(funcType, apparentType, callSignatures.Length, numConstructSignatures))
        {
            // The unknownType indicates that an error already occurred (and was reported).  No
            // need to report another error in this case.
            if (!IsErrorType(funcType) && node.TypeArguments() != null)
            {
                Error(node, Diagnostics.Untyped_function_calls_may_not_accept_type_arguments);
            }
            return ResolveUntypedCall(node);
        }
        // If FuncExpr's apparent type(section 3.8.1) is a function type, the call is a typed function call.
        // TypeScript employs overload resolution in typed function calls in order to support functions
        // with multiple call signatures.
        if (callSignatures.Length == 0)
        {
            if (numConstructSignatures != 0)
            {
                Error(node, Diagnostics.Value_of_type_0_is_not_callable_Did_you_mean_to_include_new, TypeToString(funcType));
            }
            else
            {
                Diagnostic? relatedInformation = null;
                if ((node.Arguments() ?? []).Length == 1)
                {
                    var text = GetSourceFileOfNode(node)!.Text();
                    var options = new TypeScript.Scanner.SkipTriviaOptions { StopAfterLineBreak = true };
                    if (IsLineBreak(text[SkipTriviaEx(text, node.Expression()!.End(), options) - 1]))
                    {
                        relatedInformation = CreateDiagnosticForNode(node.Expression()!, Diagnostics.Are_you_missing_a_semicolon);
                    }
                }
                InvocationError(node.Expression()!, apparentType, SignatureKind.Call, relatedInformation);
            }
            return ResolveErrorCall(node);
        }
        // When a call to a generic function is an argument to an outer call to a generic function for which
        // inference is in process, we have a choice to make. If the inner call relies on inferences made from
        // its contextual type to its return type, deferring the inner call processing allows the best possible
        // contextual type to accumulate. But if the outer call relies on inferences made from the return type of
        // the inner call, the inner call should be processed early. There's no sure way to know which choice is
        // right (only a full unification algorithm can determine that), so we resort to the following heuristic:
        // If no type arguments are specified in the inner call and at least one call signature is generic and
        // returns a function type, we choose to defer processing. This narrowly permits function composition
        // operators to flow inferences through return types, but otherwise processes calls right away. We
        // use the resolvingSignature singleton to indicate that we deferred processing. This result will be
        // propagated out and eventually turned into silentNeverType (a type that is assignable to anything and
        // from which we never make inferences).
        if ((checkMode & CheckMode.SkipGenericFunctions) != 0 && (node.TypeArguments()?.Length ?? 0) == 0 && Some(callSignatures, s => IsGenericFunctionReturningFunction(s)))
        {
            SkippedGenericFunction(node, checkMode);
            return _resolvingSignature!;
        }
        return ResolveCall(node, callSignatures, ref candidatesOutArray, checkMode, callChainFlags, null);
    }

    internal Signature ResolveNewExpression(Node node, ref Signature[]? candidatesOutArray, CheckMode checkMode)
    {
        var expressionType = CheckNonNullExpression(node.Expression()!);
        if (expressionType == _silentNeverType)
        {
            return _silentNeverSignature!;
        }
        // If expressionType's apparent type(section 3.8.1) is an object type with one or
        // more construct signatures, the expression is processed in the same manner as a
        // function call, but using the construct signatures as the initial set of candidate
        // signatures for overload resolution. The result type of the function call becomes
        // the result type of the operation.
        expressionType = GetApparentTypeWorker(expressionType);
        if (IsErrorType(expressionType))
        {
            // Another error has already been reported
            return ResolveErrorCall(node);
        }
        // TS 1.0 spec: 4.11
        // If expressionType is of type Any, Args can be any argument
        // list and the result of the operation is of type Any.
        if (IsTypeAny(expressionType))
        {
            if ((node.TypeArguments()?.Length ?? 0) != 0)
            {
                Error(node, Diagnostics.Untyped_function_calls_may_not_accept_type_arguments);
            }
            return ResolveUntypedCall(node);
        }
        // Technically, this signatures list may be incomplete. We are taking the apparent type,
        // but we are not including construct signatures that may have been added to the Object or
        // Function interface, since they have none by default. This is a bit of a leap of faith
        // that the user will not add any.
        var constructSignatures = GetSignaturesOfTypeWorker(expressionType, SignatureKind.Construct) ?? [];
        if (constructSignatures.Length != 0)
        {
            var accessibilityError = GetConstructorAccessibilityError(node, constructSignatures, ModifierFlags.NonPublicAccessibilityModifier);
            if (accessibilityError != null)
            {
                if ((accessibilityError._kind & ModifierFlags.Private) != 0)
                {
                    Error(node, Diagnostics.Constructor_of_class_0_is_private_and_only_accessible_within_the_class_declaration, TypeToString(accessibilityError._declaringClass!));
                }
                if ((accessibilityError._kind & ModifierFlags.Protected) != 0)
                {
                    Error(node, Diagnostics.Constructor_of_class_0_is_protected_and_only_accessible_within_the_class_declaration, TypeToString(accessibilityError._declaringClass!));
                }
                return ResolveErrorCall(node);
            }
            // If the expression is a class of abstract type, or an abstract construct signature,
            // then it cannot be instantiated.
            // In the case of a merged class-module or class-interface declaration,
            // only the class declaration node will have the Abstract flag set.
            if (SomeSignature(constructSignatures, sig => (sig._flags & SignatureFlags.Abstract) != 0))
            {
                Error(node, Diagnostics.Cannot_create_an_instance_of_an_abstract_class);
                return ResolveErrorCall(node);
            }
            if (expressionType._symbol != null)
            {
                var valueDecl = GetClassLikeDeclarationOfSymbol(expressionType._symbol);
                if (valueDecl != null && HasModifier(valueDecl, ModifierFlags.Abstract))
                {
                    Error(node, Diagnostics.Cannot_create_an_instance_of_an_abstract_class);
                    return ResolveErrorCall(node);
                }
            }
            return ResolveCall(node, constructSignatures, ref candidatesOutArray, checkMode, SignatureFlags.None, null);
        }
        // If expressionType's apparent type is an object type with no construct signatures but
        // one or more call signatures, the expression is processed as a function call. A compile-time
        // error occurs if the result of the function call is not Void. The type of the result of the
        // operation is Any. It is an error to have a Void this type.
        var callSignatures = GetSignaturesOfTypeWorker(expressionType, SignatureKind.Call) ?? [];
        if (callSignatures.Length != 0)
        {
            var signature = ResolveCall(node, callSignatures, ref candidatesOutArray, checkMode, SignatureFlags.None, null);
            if (!_noImplicitAny)
            {
                if (signature._declaration != null && GetReturnTypeOfSignatureWorker(signature) != _voidType)
                {
                    Error(node, Diagnostics.Only_a_void_function_can_be_called_with_the_new_keyword);
                }
                if (GetThisTypeOfSignature(signature) == _voidType)
                {
                    Error(node, Diagnostics.A_function_that_is_called_with_the_new_keyword_cannot_have_a_this_type_that_is_void);
                }
            }
            return signature;
        }
        InvocationError(node.Expression()!, expressionType, SignatureKind.Construct, null);
        return ResolveErrorCall(node);
    }

    internal sealed class ConstructorAccessibilityError
    {
        internal ModifierFlags _kind;
        internal Type? _declaringClass;
    }

    internal ConstructorAccessibilityError? GetConstructorAccessibilityError(Node node, Signature[] signatures, ModifierFlags modifiersMask)
    {
        foreach (var signature in signatures)
        {
            if (signature._declaration == null)
            {
                continue;
            }
            var declaration = signature._declaration;
            var modifiers = GetSelectedModifierFlags(declaration, modifiersMask);
            // (1) Public constructors and (2) constructor functions are always accessible.
            if (modifiers == 0 || !IsConstructorDeclaration(declaration))
            {
                continue;
            }
            var declaringClassDeclaration = GetClassLikeDeclarationOfSymbol(declaration.Parent!.Symbol);
            // A private or protected constructor can only be instantiated within its own class (or a subclass, for protected)
            if (!IsNodeWithinClass(node, declaringClassDeclaration))
            {
                var containingClass = GetContainingClass(node);
                if (containingClass != null && (modifiers & ModifierFlags.Protected) != 0)
                {
                    var containingType = GetTypeOfNode(containingClass);
                    if (TypeHasProtectedAccessibleBase(declaration.Parent!.Symbol, containingType))
                    {
                        continue;
                    }
                }
                return new ConstructorAccessibilityError
                {
                    _kind = modifiers,
                    _declaringClass = GetDeclaredTypeOfSymbolWorker(declaration.Parent!.Symbol!),
                };
            }
        }
        return null;
    }

    internal bool TypeHasProtectedAccessibleBase(Symbol? target, Type t)
    {
        var baseTypes = GetBaseTypesWorker(GetTargetTypeWorker(t)!) ?? [];
        if (baseTypes.Length == 0)
        {
            return false;
        }
        var firstBase = baseTypes[0];
        if ((firstBase._flags & TypeFlags.Intersection) != 0)
        {
            var types = firstBase.AsIntersectionType()!._types!;
            var (mixinFlags, _) = FindMixins(types);
            var intersectionMembers = firstBase.Types();
            for (var i = 0; i < intersectionMembers.Length; i++)
            {
                var intersectionMember = intersectionMembers[i];
                // We want to ignore mixin ctors
                if (!mixinFlags[i])
                {
                    if ((intersectionMember._objectFlags & (ObjectFlags.Class | ObjectFlags.Interface)) != 0)
                    {
                        if (intersectionMember._symbol == target)
                        {
                            return true;
                        }
                        if (TypeHasProtectedAccessibleBase(target, intersectionMember))
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }
        if (firstBase._symbol == target)
        {
            return true;
        }
        return TypeHasProtectedAccessibleBase(target, firstBase);
    }

    internal static bool SomeSignature(Signature[] signatures, Func<Signature, bool> f)
    {
        foreach (var sig in signatures)
        {
            if (sig._composite != null && sig._composite._isUnion && Some(sig._composite._signatures, f) || sig._composite == null && f(sig))
            {
                return true;
            }
        }
        return false;
    }

    internal Signature ResolveTaggedTemplateExpression(Node node, ref Signature[]? candidatesOutArray, CheckMode checkMode)
    {
        var tag = node.AsTaggedTemplateExpression()!.Tag!;
        var tagType = CheckExpression(tag);
        var apparentType = GetApparentTypeWorker(tagType);
        if (IsErrorType(apparentType))
        {
            // Another error has already been reported
            return ResolveErrorCall(node);
        }
        var callSignatures = GetSignaturesOfTypeWorker(apparentType, SignatureKind.Call) ?? [];
        var numConstructSignatures = GetSignaturesOfTypeWorker(apparentType, SignatureKind.Construct)?.Length ?? 0;
        if (IsUntypedFunctionCall(tagType, apparentType, callSignatures.Length, numConstructSignatures))
        {
            return ResolveUntypedCall(node);
        }
        if (callSignatures.Length == 0)
        {
            if (IsArrayLiteralExpression(node.Parent!))
            {
                Error(tag, Diagnostics.It_is_likely_that_you_are_missing_a_comma_to_separate_these_two_template_expressions_They_form_a_tagged_template_expression_which_cannot_be_invoked);
                return ResolveErrorCall(node);
            }
            InvocationError(tag, apparentType, SignatureKind.Call, null);
            return ResolveErrorCall(node);
        }
        return ResolveCall(node, callSignatures, ref candidatesOutArray, checkMode, SignatureFlags.None, null);
    }

    internal Signature ResolveDecorator(Node node, ref Signature[]? candidatesOutArray, CheckMode checkMode)
    {
        if (!CanHaveDecorators(node.Parent))
        {
            return ResolveErrorCall(node);
        }
        var funcType = CheckExpression(node.Expression()!);
        var apparentType = GetApparentTypeWorker(funcType);
        if (IsErrorType(apparentType))
        {
            return ResolveErrorCall(node);
        }
        var callSignatures = GetSignaturesOfTypeWorker(apparentType, SignatureKind.Call) ?? [];
        var numConstructSignatures = GetSignaturesOfTypeWorker(apparentType, SignatureKind.Construct)?.Length ?? 0;
        if (IsUntypedFunctionCall(funcType, apparentType, callSignatures.Length, numConstructSignatures))
        {
            return ResolveUntypedCall(node);
        }
        if (IsPotentiallyUncalledDecorator(node, callSignatures) && !IsParenthesizedExpression(node.Expression()!))
        {
            var nodeStr = GetTextOfNode(node.Expression()!);
            Error(node, Diagnostics.X_0_accepts_too_few_arguments_to_be_used_as_a_decorator_here_Did_you_mean_to_call_it_first_and_write_0, nodeStr);
            return ResolveErrorCall(node);
        }
        var headMessage = GetDiagnosticHeadMessageForDecoratorResolution(node);
        if (callSignatures.Length == 0)
        {
            var diag = NewDiagnosticChain(InvocationErrorDetails(node.Expression()!, apparentType, SignatureKind.Call), headMessage);
            diag = AddDiagnostic(diag!);
            InvocationErrorRecovery(apparentType, SignatureKind.Call, diag);
            return ResolveErrorCall(node);
        }
        var decoratorSignature = GetDecoratorCallSignature(node);
        if (decoratorSignature == null)
        {
            return ResolveErrorCall(node);
        }
        return ResolveCall(node, callSignatures, ref candidatesOutArray, checkMode, SignatureFlags.None, headMessage);
    }

    // Sometimes, we have a decorator that could accept zero arguments,
    // but is receiving too many arguments as part of the decorator invocation.
    // In those cases, a user may have meant to *call* the expression before using it as a decorator.
    internal bool IsPotentiallyUncalledDecorator(Node decorator, Signature[] signatures)
    {
        return signatures.Length != 0 && Every(signatures, sig =>
        {
            return sig._minArgumentCount == 0 && !SignatureHasRestParameter(sig) && sig.Parameters().Length < GetDecoratorArgumentCount(decorator, sig);
        });
    }

    // Gets the localized diagnostic head message to use for errors when resolving a decorator as a call expression.
    internal Message GetDiagnosticHeadMessageForDecoratorResolution(Node node)
    {
        switch (node.Parent!.Kind)
        {
            case Kind.ClassDeclaration:
            case Kind.ClassExpression:
                return Diagnostics.Unable_to_resolve_signature_of_class_decorator_when_called_as_an_expression;
            case Kind.Parameter:
                return Diagnostics.Unable_to_resolve_signature_of_parameter_decorator_when_called_as_an_expression;
            case Kind.PropertyDeclaration:
                return Diagnostics.Unable_to_resolve_signature_of_property_decorator_when_called_as_an_expression;
            case Kind.MethodDeclaration:
            case Kind.GetAccessor:
            case Kind.SetAccessor:
                return Diagnostics.Unable_to_resolve_signature_of_method_decorator_when_called_as_an_expression;
        }
        Debug.Fail("Unhandled case in getDiagnosticHeadMessageForDecoratorResolution");
        throw new System.Diagnostics.UnreachableException();
    }

    internal Signature ResolveInstanceofExpression(Node node, ref Signature[]? candidatesOutArray, CheckMode checkMode)
    {
        // if rightType is an object type with a custom `[Symbol.hasInstance]` method, then it is potentially
        // valid on the right-hand side of the `instanceof` operator. This allows normal `object` types to
        // participate in `instanceof`, as per Step 2 of https://tc39.es/ecma262/#sec-instanceofoperator.
        var right = node.AsBinaryExpression()!.Right!;
        var rightType = CheckExpression(right);
        if (!IsTypeAny(rightType))
        {
            var hasInstanceMethodType = GetSymbolHasInstanceMethodOfObjectType(rightType);
            if (hasInstanceMethodType != null)
            {
                var apparentType = GetApparentTypeWorker(hasInstanceMethodType);
                if (IsErrorType(apparentType))
                {
                    return ResolveErrorCall(node);
                }
                var callSignatures = GetSignaturesOfTypeWorker(apparentType, SignatureKind.Call) ?? [];
                var constructSignatures = GetSignaturesOfTypeWorker(apparentType, SignatureKind.Construct) ?? [];
                if (IsUntypedFunctionCall(hasInstanceMethodType, apparentType, callSignatures.Length, constructSignatures.Length))
                {
                    return ResolveUntypedCall(node);
                }
                if (callSignatures.Length != 0)
                {
                    return ResolveCall(node, callSignatures, ref candidatesOutArray, checkMode, SignatureFlags.None, null);
                }
            }
            else if (!(TypeHasCallOrConstructSignaturesWorker(rightType) || IsTypeSubtypeOf(rightType, _globalFunctionType!)))
            {
                Error(right, Diagnostics.The_right_hand_side_of_an_instanceof_expression_must_be_either_of_type_any_a_class_function_or_other_type_assignable_to_the_Function_interface_type_or_an_object_type_with_a_Symbol_hasInstance_method);
                return ResolveErrorCall(node);
            }
        }
        // fall back to a default signature
        return _anySignature!;
    }
}
