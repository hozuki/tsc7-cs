// ported from: internal/checker/checker.go:9839-10223（调用解析的错误报告：reportCallResolutionErrors、
//   addImplementationSuccessElaboration、getArgumentArityError、isPromiseResolveArityError、getErrorNodeForCallNode、
//   getTypeArgumentArityError、reportCannotInvokePossiblyNullOrUndefinedError、resolveUntypedCall、resolveErrorCall、
//   isUntypedFunctionCall、invocationErrorDetails、invocationError、invocationErrorRecovery、
//   isGenericFunctionReturningFunction、skippedGenericFunction）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) 包级函数 `getErrorNodeForCallNode` → 同一个 partial 类里的 `internal static`（§5.5）。Go 里它把参数 node
//    重新赋值后返回，C# 形参只读语义不受影响（局部变量承接，行为一致）。
// 2) Worker 改名（artifacts/checker-method-collisions.md）：`getSignaturesOfType` → `GetSignaturesOfTypeWorker`、
//    `getReturnTypeOfSignature` → `GetReturnTypeOfSignatureWorker`、`getResolvedSymbol` → `GetResolvedSymbolWorker`、
//    `getAwaitedType` → `GetAwaitedTypeWorker`、`hasEffectiveRestParameter` → `HasEffectiveRestParameterWorker`、
//    `getMinTypeArgumentCount` → `GetMinTypeArgumentCountWorker`。
// 3) `math.MaxInt`/`math.MinInt` → `int.MaxValue`/`int.MinValue`；`strconv.Itoa(n)` → `n.ToString()`
//    （Go 输出 ASCII 十进制；C# 的 int.ToString() 对数字用不变形 digits，等价）。
// 4) Go 的 `switch { case A: … case B: … }`（无标签 switch）→ if/else-if 链（C# 的 switch 不能这样用）。
// 5) `panic(...)` → `Debug.Fail(...)` + `throw new System.Diagnostics.UnreachableException()`。
// 6) Go 的 `if x := …; x != nil` 语句内声明 → C# 里拆成两条语句（`var x = …; if (x != null)`）。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Scanner.Scanner;

public sealed partial class Checker
{
    internal void ReportCallResolutionErrors(Node node, CallState s, Signature[] signatures, Message? headMessage)
    {
        if (s._candidatesForArgumentError != null && s._candidatesForArgumentError.Length != 0)
        {
            var last = s._candidatesForArgumentError[^1];
            // Go: `var diags []*ast.Diagnostic; c.isSignatureApplicable(..., &diags)` → 指针非 nil、切片为 nil。
            // C# 里 null 表示 Go 的 nil 指针（→ AddDiagnostic），所以收集器必须以非 null 空数组起步，
            // 否则下面 `foreach (var initialDiagnostic in diags ?? [])` 拿不到诊断，headMessage/重载包装全部丢失。
            Diagnostic[]? diags = [];
            IsSignatureApplicable(s._node!, s._args, last, _assignableRelation!, CheckMode.Normal, true /*reportErrors*/, ref diags);
            foreach (var initialDiagnostic in diags ?? [])
            {
                var diagnostic = initialDiagnostic;
                if (s._candidatesForArgumentError.Length > 1)
                {
                    diagnostic = NewDiagnosticChain(diagnostic, Diagnostics.The_last_overload_gave_the_following_error)!;
                    diagnostic = NewDiagnosticChain(diagnostic, Diagnostics.No_overload_matches_this_call)!;
                }
                if (headMessage != null)
                {
                    diagnostic = NewDiagnosticChain(diagnostic, headMessage)!;
                }
                if (last._declaration != null && s._candidatesForArgumentError.Length > 1)
                {
                    diagnostic.AddRelatedInfo(NewDiagnosticForNode(last._declaration, Diagnostics.The_last_overload_is_declared_here));
                }
                AddImplementationSuccessElaboration(s, last, diagnostic);
                AddDiagnostic(diagnostic);
            }
        }
        else if (s._candidateForArgumentArityError != null)
        {
            AddDiagnostic(GetArgumentArityError(s._node!, [s._candidateForArgumentArityError], s._args, headMessage));
        }
        else if (s._candidateForTypeArgumentError != null)
        {
            CheckTypeArguments(s._candidateForTypeArgumentError, s._node!.TypeArguments(), true /*reportErrors*/, headMessage);
        }
        else if (!IsJsxOpeningFragment(node))
        {
            var signaturesWithCorrectTypeArgumentArity = Filter(signatures, sig => HasCorrectTypeArgumentArity(sig, s._typeArguments ?? []));
            if (signaturesWithCorrectTypeArgumentArity.Length == 0)
            {
                AddDiagnostic(GetTypeArgumentArityError(s._node!, signatures, s._typeArguments ?? [], headMessage));
            }
            else
            {
                AddDiagnostic(GetArgumentArityError(s._node!, signaturesWithCorrectTypeArgumentArity, s._args, headMessage));
            }
        }
    }

    internal void AddImplementationSuccessElaboration(CallState s, Signature failed, Diagnostic diagnostic)
    {
        if (failed._declaration != null && failed._declaration.Symbol != null)
        {
            var declarations = failed._declaration.Symbol!.Declarations ?? [];
            if (declarations.Length > 1)
            {
                var implementation = Find(declarations, d => IsFunctionLikeDeclaration(d) && NodeIsPresent(d.Body()));
                if (implementation != null)
                {
                    var candidate = GetSignatureFromDeclarationWorker(implementation);
                    var localState = s.ShallowClone();
                    localState._candidates = [candidate];
                    localState._isSingleNonGenericCandidate = candidate.TypeParameters().Length == 0;
                    if (ChooseOverload(localState, _assignableRelation!) != null)
                    {
                        diagnostic.AddRelatedInfo(NewDiagnosticForNode(implementation, Diagnostics.The_call_would_have_succeeded_against_this_implementation_but_implementation_signatures_of_overloads_are_not_externally_visible));
                    }
                }
            }
        }
    }

    internal Diagnostic GetArgumentArityError(Node node, Signature[] signatures, Node[] args, Message? headMessage)
    {
        var spreadIndex = GetSpreadArgumentIndex(args);
        if (spreadIndex > -1)
        {
            return NewDiagnosticForNode(args[spreadIndex], Diagnostics.A_spread_argument_must_either_have_a_tuple_type_or_be_passed_to_a_rest_parameter);
        }
        var minCount = int.MaxValue; // smallest parameter count
        var maxCount = int.MinValue; // largest parameter count
        var maxBelow = int.MinValue; // largest parameter count that is smaller than the number of arguments
        var minAbove = int.MaxValue; // smallest parameter count that is larger than the number of arguments
        Signature? closestSignature = null;
        foreach (var sig in signatures)
        {
            var minParameter = GetMinArgumentCount(sig);
            var maxParameter = GetParameterCount(sig);
            // smallest/largest parameter counts
            if (minParameter < minCount)
            {
                minCount = minParameter;
                closestSignature = sig;
            }
            maxCount = Math.Max(maxCount, maxParameter);
            // shortest parameter count *longer than the call*/longest parameter count *shorter than the call*
            if (minParameter < args.Length && minParameter > maxBelow)
            {
                maxBelow = minParameter;
            }
            if (args.Length < maxParameter && maxParameter < minAbove)
            {
                minAbove = maxParameter;
            }
        }
        var hasRestParameter = Some(signatures, s => HasEffectiveRestParameterWorker(s));
        string parameterRange;
        if (hasRestParameter)
        {
            parameterRange = minCount.ToString();
        }
        else if (minCount < maxCount)
        {
            parameterRange = minCount.ToString() + "-" + maxCount.ToString();
        }
        else
        {
            parameterRange = minCount.ToString();
        }
        var isVoidPromiseError = !hasRestParameter && parameterRange == "1" && args.Length == 0 && IsPromiseResolveArityError(node);
        var errorNode = GetErrorNodeForCallNode(node);
        if (isVoidPromiseError && IsInJSFile(node))
        {
            return NewDiagnosticForNode(errorNode, Diagnostics.Expected_1_argument_but_got_0_new_Promise_needs_a_JSDoc_hint_to_produce_a_resolve_that_can_be_called_without_arguments);
        }
        Message message;
        if (IsDecorator(node))
        {
            if (hasRestParameter)
            {
                message = Diagnostics.The_runtime_will_invoke_the_decorator_with_1_arguments_but_the_decorator_expects_at_least_0;
            }
            else
            {
                message = Diagnostics.The_runtime_will_invoke_the_decorator_with_1_arguments_but_the_decorator_expects_0;
            }
        }
        else if (hasRestParameter)
        {
            message = Diagnostics.Expected_at_least_0_arguments_but_got_1;
        }
        else if (isVoidPromiseError)
        {
            message = Diagnostics.Expected_0_arguments_but_got_1_Did_you_forget_to_include_void_in_your_type_argument_to_Promise;
        }
        else
        {
            message = Diagnostics.Expected_0_arguments_but_got_1;
        }
        if (minCount < args.Length && args.Length < maxCount)
        {
            // between min and max, but with no matching overload
            Diagnostic diagnostic = NewDiagnosticForNode(errorNode, Diagnostics.No_overload_expects_0_arguments_but_overloads_do_exist_that_expect_either_1_or_2_arguments, args.Length, maxBelow, minAbove);
            if (headMessage != null)
            {
                diagnostic = NewDiagnosticChain(diagnostic, headMessage)!;
            }
            return diagnostic;
        }
        if (args.Length < minCount)
        {
            // too short: put the error span on the call expression, not any of the args
            Diagnostic diagnostic = NewDiagnosticForNode(errorNode, message, parameterRange, args.Length);
            if (headMessage != null)
            {
                diagnostic = NewDiagnosticChain(diagnostic, headMessage)!;
            }
            Node? parameter = null;
            if (closestSignature != null && closestSignature._declaration != null)
            {
                parameter = ElementOrNil(closestSignature._declaration.Parameters(), args.Length + IfElse(closestSignature._thisParameter != null, 1, 0));
            }
            if (parameter != null)
            {
                Diagnostic related;
                if (IsBindingPattern(parameter.Name()))
                {
                    related = NewDiagnosticForNode(parameter, Diagnostics.An_argument_matching_this_binding_pattern_was_not_provided);
                }
                else if (IsRestParameter(parameter))
                {
                    related = NewDiagnosticForNode(parameter, Diagnostics.Arguments_for_the_rest_parameter_0_were_not_provided, parameter.Name()!.Text());
                }
                else
                {
                    related = NewDiagnosticForNode(parameter, Diagnostics.An_argument_for_0_was_not_provided, parameter.Name()!.Text());
                }
                diagnostic.AddRelatedInfo(related);
            }
            return diagnostic;
        }
        else
        {
            // Guard against out-of-bounds access when maxCount >= len(args).
            // This can happen when we reach this fallback error path but the argument
            // count actually matches the parameter count (e.g., due to trailing commas
            // causing signature resolution to fail for other reasons).
            if (maxCount >= args.Length)
            {
                Diagnostic guardDiagnostic = NewDiagnosticForNode(errorNode, message, parameterRange, args.Length);
                if (headMessage != null)
                {
                    guardDiagnostic = NewDiagnosticChain(guardDiagnostic, headMessage)!;
                }
                return guardDiagnostic;
            }
            var sourceFile = GetSourceFileOfNode(node)!;
            var pos = args[maxCount].Pos();
            var end = args[^1].End();
            if (end == pos)
            {
                end++;
            }
            pos = SkipTrivia(sourceFile.Text(), pos);
            if (end < pos)
            {
                end = pos;
            }
            Diagnostic diagnostic = NewDiagnostic(sourceFile, NewTextRange(pos, end), message, parameterRange, args.Length);
            if (headMessage != null)
            {
                diagnostic = NewDiagnosticChain(diagnostic, headMessage)!;
            }
            return diagnostic;
        }
    }

    internal bool IsPromiseResolveArityError(Node node)
    {
        if (!IsCallExpression(node) || !IsIdentifier(node.Expression()!))
        {
            return false;
        }
        var symbol = _resolveName(node.Expression()!, node.Expression()!.Text(), SymbolFlags.Value, null /*nameNotFoundMessage*/, false /*isUse*/, false);
        if (symbol == null)
        {
            return false;
        }
        var decl = symbol.ValueDeclaration;
        if (decl == null || !IsParameterDeclaration(decl) || !IsFunctionExpressionOrArrowFunction(decl.Parent) || !IsNewExpression(decl.Parent!.Parent!) || !IsIdentifier(decl.Parent!.Parent!.Expression()!))
        {
            return false;
        }
        var globalPromiseSymbol = _getGlobalPromiseConstructorSymbolOrNil();
        if (globalPromiseSymbol == null)
        {
            return false;
        }
        var constructorSymbol = GetResolvedSymbolWorker(decl.Parent!.Parent!.Expression()!);
        return constructorSymbol == globalPromiseSymbol;
    }

    internal static Node GetErrorNodeForCallNode(Node node)
    {
        var result = node;
        if (IsCallExpression(result))
        {
            result = result.Expression()!;
            if (IsPropertyAccessExpression(result))
            {
                result = result.Name()!;
            }
        }
        return result;
    }

    internal Diagnostic GetTypeArgumentArityError(Node node, Signature[] signatures, Node[] typeArguments, Message? headMessage)
    {
        Diagnostic diagnostic;
        var argCount = typeArguments.Length;
        var sourceFile = GetSourceFileOfNode(node)!;
        var typeArgumentList = node.TypeArgumentList()!;
        var loc = NewTextRange(SkipTrivia(sourceFile.Text(), typeArgumentList.Loc.Pos()), typeArgumentList.Loc.End());
        if (signatures.Length == 1)
        {
            // No overloads exist
            var sig = signatures[0];
            var minCount = GetMinTypeArgumentCountWorker(sig._typeParameters);
            var maxCount = sig.TypeParameters().Length;
            var expected = minCount.ToString();
            if (minCount < maxCount)
            {
                expected = expected + "-" + maxCount.ToString();
            }
            diagnostic = NewDiagnostic(sourceFile, loc, Diagnostics.Expected_0_type_arguments_but_got_1, expected, argCount);
        }
        else
        {
            // Overloads exist
            var belowArgCount = int.MinValue;
            var aboveArgCount = int.MaxValue;
            foreach (var sig in signatures)
            {
                var minCount = GetMinTypeArgumentCountWorker(sig._typeParameters);
                var maxCount = sig.TypeParameters().Length;
                if (minCount > argCount)
                {
                    aboveArgCount = Math.Min(aboveArgCount, minCount);
                }
                else if (maxCount < argCount)
                {
                    belowArgCount = Math.Max(belowArgCount, maxCount);
                }
            }
            if (belowArgCount != int.MinValue && aboveArgCount != int.MaxValue)
            {
                diagnostic = NewDiagnostic(sourceFile, loc, Diagnostics.No_overload_expects_0_type_arguments_but_overloads_do_exist_that_expect_either_1_or_2_type_arguments, argCount, belowArgCount, aboveArgCount);
            }
            else
            {
                diagnostic = NewDiagnostic(sourceFile, loc, Diagnostics.Expected_0_type_arguments_but_got_1, IfElse(belowArgCount == int.MinValue, aboveArgCount, belowArgCount), argCount);
            }
        }
        if (headMessage != null)
        {
            diagnostic = NewDiagnosticChain(diagnostic, headMessage)!;
        }
        return diagnostic;
    }

    internal void ReportCannotInvokePossiblyNullOrUndefinedError(Node node, TypeFacts facts)
    {
        Error(node, IfElse((facts & TypeFacts.IsUndefined) != 0,
            IfElse((facts & TypeFacts.IsNull) != 0,
                Diagnostics.Cannot_invoke_an_object_which_is_possibly_null_or_undefined,
                Diagnostics.Cannot_invoke_an_object_which_is_possibly_undefined),
            Diagnostics.Cannot_invoke_an_object_which_is_possibly_null));
    }

    internal Signature ResolveUntypedCall(Node node)
    {
        if (CallLikeExpressionMayHaveTypeArguments(node))
        {
            // Check type arguments even though we will give an error that untyped calls may not accept type arguments.
            // This gets us diagnostics for the type arguments and marks them as referenced.
            CheckSourceElements(node.TypeArguments() ?? []);
        }
        switch (node.Kind)
        {
            case Kind.TaggedTemplateExpression:
                CheckExpression(node.AsTaggedTemplateExpression()!.Template!);
                break;
            case Kind.JsxOpeningElement:
            case Kind.JsxSelfClosingElement:
                CheckExpression(node.Attributes()!);
                break;
            case Kind.BinaryExpression:
                CheckExpression(node.AsBinaryExpression()!.Left!);
                break;
            case Kind.CallExpression:
            case Kind.NewExpression:
                foreach (var argument in node.Arguments() ?? [])
                {
                    CheckExpression(argument);
                }
                break;
        }
        return _anySignature!;
    }

    internal Signature ResolveErrorCall(Node node)
    {
        ResolveUntypedCall(node);
        return _unknownSignature!;
    }

    /**
     * TS 1.0 spec: 4.12
     * If FuncExpr is of type Any, or of an object type that has no call or construct signatures
     * but is a subtype of the Function interface, the call is an untyped function call.
     */
    internal bool IsUntypedFunctionCall(Type funcType, Type apparentFuncType, int numCallSignatures, int numConstructSignatures)
    {
        // We exclude union types because we may have a union of function types that happen to have no common signatures.
        return IsTypeAny(funcType) || IsTypeAny(apparentFuncType) && (funcType._flags & TypeFlags.TypeParameter) != 0 ||
            numCallSignatures == 0 && numConstructSignatures == 0 && (apparentFuncType._flags & TypeFlags.Union) == 0 &&
                (GetReducedTypeWorker(apparentFuncType)._flags & TypeFlags.Never) == 0 && IsTypeAssignableToWorker(funcType, _globalFunctionType!);
    }

    internal Diagnostic InvocationErrorDetails(Node errorTarget, Type apparentType, SignatureKind kind)
    {
        Diagnostic? diagnostic = null;
        var isCall = kind == SignatureKind.Call;
        var awaitedType = GetAwaitedTypeWorker(apparentType);
        var maybeMissingAwait = awaitedType != null && (GetSignaturesOfTypeWorker(awaitedType, kind)?.Length ?? 0) > 0;
        var target = errorTarget;
        if (IsPropertyAccessExpression(errorTarget) && IsCallExpression(errorTarget.Parent!))
        {
            target = errorTarget.Name()!;
        }
        if ((apparentType._flags & TypeFlags.Union) != 0)
        {
            var types = apparentType.Types();
            var hasSignatures = false;
            foreach (var constituent in types)
            {
                var signatures = GetSignaturesOfTypeWorker(constituent, kind) ?? [];
                if (signatures.Length != 0)
                {
                    hasSignatures = true;
                    if (diagnostic != null)
                    {
                        // Bail early if we already have an error, no chance of "No constituent of type is callable"
                        break;
                    }
                }
                else
                {
                    // Error on the first non callable constituent only
                    if (diagnostic == null)
                    {
                        diagnostic = NewDiagnosticForNode(target, IfElse(isCall, Diagnostics.Type_0_has_no_call_signatures, Diagnostics.Type_0_has_no_construct_signatures), TypeToString(constituent));
                        diagnostic = NewDiagnosticChainForNode(diagnostic, target, IfElse(isCall, Diagnostics.Not_all_constituents_of_type_0_are_callable, Diagnostics.Not_all_constituents_of_type_0_are_constructable), TypeToString(apparentType));
                    }
                    if (hasSignatures)
                    {
                        // Bail early if we already found a signature, no chance of "No constituent of type is callable"
                        break;
                    }
                }
            }
            if (!hasSignatures)
            {
                diagnostic = NewDiagnosticForNode(target, IfElse(isCall, Diagnostics.No_constituent_of_type_0_is_callable, Diagnostics.No_constituent_of_type_0_is_constructable), TypeToString(apparentType));
            }
            if (diagnostic == null)
            {
                diagnostic = NewDiagnosticForNode(target, IfElse(isCall, Diagnostics.Each_member_of_the_union_type_0_has_signatures_but_none_of_those_signatures_are_compatible_with_each_other, Diagnostics.Each_member_of_the_union_type_0_has_construct_signatures_but_none_of_those_signatures_are_compatible_with_each_other), TypeToString(apparentType));
            }
        }
        else
        {
            diagnostic = NewDiagnosticChainForNode(diagnostic, target, IfElse(isCall, Diagnostics.Type_0_has_no_call_signatures, Diagnostics.Type_0_has_no_construct_signatures), TypeToString(apparentType));
        }
        var headMessage = IfElse(isCall, Diagnostics.This_expression_is_not_callable, Diagnostics.This_expression_is_not_constructable);
        // Diagnose get accessors incorrectly called as functions
        if (IsCallExpression(errorTarget.Parent!) && (errorTarget.Parent!.Arguments() ?? []).Length == 0)
        {
            var resolvedSymbol = GetResolvedSymbolOrNil(errorTarget);
            if (resolvedSymbol != null && (resolvedSymbol.Flags & SymbolFlags.GetAccessor) != 0)
            {
                headMessage = Diagnostics.This_expression_is_not_callable_because_it_is_a_get_accessor_Did_you_mean_to_use_it_without;
            }
        }
        diagnostic = NewDiagnosticChainForNode(diagnostic, target, headMessage)!;
        if (maybeMissingAwait)
        {
            diagnostic.AddRelatedInfo(NewDiagnosticForNode(errorTarget, Diagnostics.Did_you_forget_to_use_await));
        }
        return diagnostic;
    }

    internal void InvocationError(Node errorTarget, Type apparentType, SignatureKind kind, Diagnostic? relatedInformation)
    {
        var diagnostic = InvocationErrorDetails(errorTarget, apparentType, kind);
        if (relatedInformation != null)
        {
            diagnostic.AddRelatedInfo(relatedInformation);
        }
        diagnostic = AddDiagnostic(diagnostic);
        InvocationErrorRecovery(apparentType, kind, diagnostic);
    }

    internal void InvocationErrorRecovery(Type apparentType, SignatureKind kind, Diagnostic diagnostic)
    {
        if (apparentType._symbol == null)
        {
            return;
        }
        var importNode = _exportTypeLinks.Get(apparentType._symbol)._originatingImport;
        // Create a diagnostic on the originating import if possible onto which we can attach a quickfix
        //  An import call expression cannot be rewritten into another form to correct the error - the only solution is to use `.default` at the use-site
        if (importNode != null && !IsImportCall(importNode))
        {
            var sigs = GetSignaturesOfTypeWorker(GetTypeOfSymbolWorker(_exportTypeLinks.Get(apparentType._symbol)._target!), kind) ?? [];
            if (sigs.Length == 0)
            {
                return;
            }
            diagnostic.AddRelatedInfo(NewDiagnosticForNode(importNode, Diagnostics.Type_originates_at_this_import_A_namespace_style_import_cannot_be_called_or_constructed_and_will_cause_a_failure_at_runtime_Consider_using_a_default_import_or_import_require_here_instead));
        }
    }

    internal bool IsGenericFunctionReturningFunction(Signature signature)
    {
        return signature.TypeParameters().Length != 0 && IsFunctionType(GetReturnTypeOfSignatureWorker(signature));
    }

    internal void SkippedGenericFunction(Node node, CheckMode checkMode)
    {
        if ((checkMode & CheckMode.Inferential) != 0)
        {
            // We have skipped a generic function during inferential typing. Obtain the inference context and
            // indicate this has occurred such that we know a second pass of inference is be needed.
            var context = GetInferenceContext(node)!;
            context._flags |= InferenceFlags.SkippedGenericFunction;
        }
    }
}
