// ported from: internal/checker/checker.go:10224-10458（标签模板/括号/类表达式/函数表达式与方法字面量的检查，
//   以及上下文签名下参数类型的推入：checkTaggedTemplateExpression、checkParenthesizedExpression、
//   checkClassExpression、getFirstTransformableStaticClassElement、checkClassExpressionExternalHelpers、
//   checkClassExpressionDeferred、checkFunctionExpressionOrObjectLiteralMethod、
//   contextuallyCheckFunctionExpressionOrObjectLiteralMethod、checkFunctionExpressionOrObjectLiteralMethodDeferred、
//   inferFromAnnotatedParametersAndReturn）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) Worker 改名（artifacts/checker-method-collisions.md）：`getResolvedSignature` → `GetResolvedSignatureWorker`、
//    `getReturnTypeOfSignature` → `GetReturnTypeOfSignatureWorker`、`getTypeOfSymbol` → `GetTypeOfSymbolWorker`、
//    `getSignaturesOfType` → `GetSignaturesOfTypeWorker`、`getTypeFromTypeNode` → `GetTypeFromTypeNodeWorker`、
//    `getTypeAtPosition` → `GetTypeAtPositionWorker`。
// 2) Go 的 `c.getResolvedSignature(node, nil, …)`（字面 nil 出参）→ 局部变量 + `ref 局部`（C# 不允许 `ref null`）。
// 3) Go 的 `if cached, ok := c.contextFreeTypes[node]; ok { return cached }` → `TryGetValue`
//    （Go 的 map 读返回零值，Dictionary 索引器抛 KeyNotFoundException，docs/architecture.md §3.2）。
// 4) `node.FunctionLikeData().FullSignature`（Go 里 FullSignature 一定非 nil 的调用点）→ `?.FullSignature`
//    （C# 的 `FunctionLikeData()` 返回接口、可能为 null，安全取值后判空与 Go 的 nil 判断等价）。
// 5) `checkMode &^ CheckModeSkipContextSensitive` → `checkMode & ~CheckMode.SkipContextSensitive`。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;

public sealed partial class Checker
{
    internal Type CheckTaggedTemplateExpression(Node node)
    {
        if (!CheckGrammarTaggedTemplateChain(node.AsTaggedTemplateExpression()!))
        {
            CheckGrammarTypeArguments(node, node.TypeArgumentList());
        }
        Signature[]? candidatesOutArray = null;
        var signature = GetResolvedSignatureWorker(node, ref candidatesOutArray, CheckMode.Normal);
        CheckDeprecatedSignature(signature, node);
        return GetReturnTypeOfSignatureWorker(signature);
    }

    internal Type CheckParenthesizedExpression(Node node, CheckMode checkMode)
    {
        return CheckExpressionEx(node.Expression()!, checkMode);
    }

    internal Type CheckClassExpression(Node node)
    {
        CheckClassLikeDeclaration(node);
        CheckNodeDeferred(node);
        CheckClassExpressionExternalHelpers(node.AsClassExpression()!);
        return GetTypeOfSymbolWorker(GetSymbolOfDeclaration(node)!);
    }

    internal Node? GetFirstTransformableStaticClassElement(Node node)
    {
        var willTransformStaticElementsOfDecoratedClass = !_legacyDecorators &&
            _languageVersion < LanguageFeatureMinimumTarget.ClassAndClassElementDecorators &&
            ClassOrConstructorParameterIsDecorated(false, node);
        var willTransformPrivateElementsOrClassStaticBlocks = _languageVersion < LanguageFeatureMinimumTarget.PrivateNamesAndClassStaticBlocks || _languageVersion < LanguageFeatureMinimumTarget.ClassAndClassElementDecorators;
        var willTransformInitializers = !_emitStandardClassFields;
        if (willTransformStaticElementsOfDecoratedClass || willTransformPrivateElementsOrClassStaticBlocks)
        {
            foreach (var member in node.Members() ?? [])
            {
                if (willTransformStaticElementsOfDecoratedClass && ClassElementOrClassElementParameterIsDecorated(false, member, node))
                {
                    var firstDecorator = FirstOrNil(node.Decorators());
                    if (firstDecorator != null)
                    {
                        return firstDecorator;
                    }
                    return node;
                }
                else if (willTransformPrivateElementsOrClassStaticBlocks)
                {
                    if (IsClassStaticBlockDeclaration(member))
                    {
                        return member;
                    }
                    else if (IsStatic(member) &&
                        (IsPrivateIdentifierClassElementDeclaration(member) ||
                            willTransformInitializers && IsInitializedProperty(member)))
                    {
                        return member;
                    }
                }
            }
        }
        return null;
    }

    internal void CheckClassExpressionExternalHelpers(ClassExpression node)
    {
        if (node.Name() != null)
        {
            return;
        }
        var parent = WalkUpOuterExpressions(node);
        if (!IsNamedEvaluationSource(parent!))
        {
            return;
        }

        var willTransformESDecorators = !_legacyDecorators && _languageVersion < LanguageFeatureMinimumTarget.ClassAndClassElementDecorators;
        Node? location;
        if (willTransformESDecorators && ClassOrConstructorParameterIsDecorated(false, node))
        {
            location = node;
            var firstDecorator = FirstOrNil(node.Decorators());
            if (firstDecorator != null)
            {
                location = firstDecorator;
            }
        }
        else
        {
            location = GetFirstTransformableStaticClassElement(node);
        }

        if (location != null)
        {
            CheckExternalEmitHelpers(location, ExternalEmitHelpers.SetFunctionName);
            if ((IsPropertyAssignment(parent!) || IsPropertyDeclaration(parent!) || IsBindingElement(parent!)) && IsComputedPropertyName(parent!.Name()!))
            {
                CheckExternalEmitHelpers(location, ExternalEmitHelpers.PropKey);
            }
        }
    }

    internal void CheckClassExpressionDeferred(Node node)
    {
        CheckSourceElements(node.Members() ?? []);
        RegisterForUnusedIdentifiersCheck(node);
    }

    internal Type CheckFunctionExpressionOrObjectLiteralMethod(Node node, CheckMode checkMode)
    {
        CheckNodeDeferred(node);
        if (node.FunctionLikeData()?.FullSignature != null)
        {
            CheckSourceElement(node.FunctionLikeData()!.FullSignature!);
        }
        if (IsFunctionExpression(node))
        {
            CheckCollisionsForDeclarationName(node, node.Name());
        }
        if ((checkMode & CheckMode.SkipContextSensitive) != 0 && IsContextSensitiveWorker(node))
        {
            // Skip parameters, return signature with return type that retains noncontextual parts so inferences can still be drawn in an early stage
            if (node.Type() == null && !HasContextSensitiveParameters(node))
            {
                // Return plain anyFunctionType if there is no possibility we'll make inferences from the return type
                var contextualSignature = GetContextualSignature(node);
                if (contextualSignature != null && _couldContainTypeVariables(GetReturnTypeOfSignatureWorker(contextualSignature)))
                {
                    if (_contextFreeTypes.TryGetValue(node, out var cached))
                    {
                        return cached;
                    }
                    var returnType = GetReturnTypeFromBody(node, checkMode);
                    var returnOnlySignature = NewSignature(SignatureFlags.IsNonInferrable, null, null /*typeParameters*/, null /*thisParameter*/, null, returnType, null /*resolvedTypePredicate*/, 0);
                    var returnOnlyType = NewAnonymousType(node.Symbol, null, [returnOnlySignature], null, null);
                    returnOnlyType._objectFlags |= ObjectFlags.NonInferrableType;
                    _contextFreeTypes[node] = returnOnlyType;
                    return returnOnlyType;
                }
            }
            return _anyFunctionType!;
        }
        // Grammar checking
        var hasGrammarError = CheckGrammarFunctionLikeDeclaration(node);
        if (!hasGrammarError && IsFunctionExpression(node))
        {
            CheckGrammarForGenerator(node);
        }
        if (node.FunctionLikeData()?.FullSignature != null)
        {
            if (GetContextualCallSignature(GetTypeFromTypeNodeWorker(node.FunctionLikeData()!.FullSignature!), node) == null)
            {
                Error(node.FunctionLikeData()!.FullSignature!, Diagnostics.A_JSDoc_type_tag_on_a_function_must_have_a_signature_with_the_correct_number_of_arguments);
            }
        }
        ContextuallyCheckFunctionExpressionOrObjectLiteralMethod(node, checkMode);
        return GetTypeOfSymbolWorker(GetSymbolOfDeclaration(node)!);
    }

    internal void ContextuallyCheckFunctionExpressionOrObjectLiteralMethod(Node node, CheckMode checkMode)
    {
        var links = _nodeLinks.Get(node);
        // Check if function expression is contextually typed and assign parameter types if so.
        if ((links._flags & NodeCheckFlags.ContextChecked) == 0)
        {
            var contextualSignature = GetContextualSignature(node);
            // If a type check is started at a function expression that is an argument of a function call, obtaining the
            // contextual type may recursively get back to here during overload resolution of the call. If so, we will have
            // already assigned contextual types.
            if ((links._flags & NodeCheckFlags.ContextChecked) == 0)
            {
                links._flags |= NodeCheckFlags.ContextChecked;
                var signature = FirstOrNil(GetSignaturesOfTypeWorker(GetTypeOfSymbolWorker(GetSymbolOfDeclaration(node)!), SignatureKind.Call));
                if (signature == null)
                {
                    return;
                }
                if (IsContextSensitiveWorker(node))
                {
                    if (contextualSignature != null)
                    {
                        var inferenceContext = GetInferenceContext(node);
                        Signature? instantiatedContextualSignature = null;
                        if ((checkMode & CheckMode.Inferential) != 0)
                        {
                            InferFromAnnotatedParametersAndReturn(signature, contextualSignature, inferenceContext!);
                            var restType = GetEffectiveRestType(contextualSignature);
                            if (restType != null && (restType._flags & TypeFlags.TypeParameter) != 0)
                            {
                                instantiatedContextualSignature = InstantiateSignature(contextualSignature, inferenceContext!._nonFixingMapper!);
                            }
                        }
                        if (instantiatedContextualSignature == null)
                        {
                            if (inferenceContext != null)
                            {
                                instantiatedContextualSignature = InstantiateSignature(contextualSignature, inferenceContext!._mapper!);
                            }
                            else
                            {
                                instantiatedContextualSignature = contextualSignature;
                            }
                        }
                        AssignContextualParameterTypes(signature, instantiatedContextualSignature);
                    }
                    else
                    {
                        // Force resolution of all parameter types such that the absence of a contextual type is consistently reflected.
                        AssignNonContextualParameterTypes(signature);
                    }
                }
                else if (contextualSignature != null && node.TypeParameters() == null && contextualSignature.Parameters().Length > (node.Parameters() ?? []).Length)
                {
                    var inferenceContext = GetInferenceContext(node);
                    if ((checkMode & CheckMode.Inferential) != 0)
                    {
                        InferFromAnnotatedParametersAndReturn(signature, contextualSignature, inferenceContext!);
                    }
                }
                if (contextualSignature != null && GetReturnTypeFromAnnotation(node) == null && signature._resolvedReturnType == null)
                {
                    // resolvedReturnType is cached indefinitely, so the return type here has to be computed without CheckModeSkipContextSensitive;
                    // otherwise anyFunctionType could leak as part of the computed (and cached) return type.
                    var returnType = GetReturnTypeFromBody(node, checkMode & ~CheckMode.SkipContextSensitive);
                    if (signature._resolvedReturnType == null)
                    {
                        signature._resolvedReturnType = returnType;
                    }
                }
                CheckSignatureDeclaration(node);
            }
        }
    }

    internal void CheckFunctionExpressionOrObjectLiteralMethodDeferred(Node node)
    {
        var functionFlags = GetFunctionFlags(node);
        var returnType = GetReturnTypeFromAnnotation(node);
        CheckAllCodePathsInNonVoidFunctionReturnOrThrow(node, returnType);
        var body = node.Body();
        if (body != null)
        {
            if (node.Type() == null)
            {
                // There are some checks that are only performed in getReturnTypeFromBody, that may produce errors
                // we need. An example is the noImplicitAny errors resulting from widening the return expression
                // of a function. Because checking of function expression bodies is deferred, there was never an
                // appropriate time to do this during the main walk of the file (see the comment at the top of
                // checkFunctionExpressionBodies). So it must be done now.
                GetReturnTypeOfSignatureWorker(GetSignatureFromDeclarationWorker(node));
            }
            if (IsBlock(body))
            {
                CheckSourceElement(body);
            }
            else
            {
                // From within an async function you can return either a non-promise value or a promise. Any
                // Promise/A+ compatible implementation will always assimilate any foreign promise, so we
                // should not be checking assignability of a promise to the return type. Instead, we need to
                // check assignability of the awaited type of the expression body against the promised type of
                // its return type annotation.
                var exprType = CheckExpression(body);
                if (returnType != null)
                {
                    var returnOrPromisedType = UnwrapReturnType(returnType, functionFlags);
                    if (returnOrPromisedType != null)
                    {
                        CheckReturnExpression(node, returnOrPromisedType, body, body, exprType, false);
                    }
                }
            }
        }
    }

    internal void InferFromAnnotatedParametersAndReturn(Signature sig, Signature context, InferenceContext inferenceContext)
    {
        var length = sig.Parameters().Length - IfElse(SignatureHasRestParameter(sig), 1, 0);
        for (var i = 0; i < length; i++)
        {
            var declaration = sig.Parameters()[i].ValueDeclaration!;
            var typeNode = declaration.Type();
            if (typeNode != null)
            {
                var source = AddOptionalityEx(GetTypeFromTypeNodeWorker(typeNode), false /*isProperty*/, IsOptionalDeclaration(declaration));
                var target = GetTypeAtPositionWorker(context, i);
                InferTypes(inferenceContext._inferences, source, target, InferencePriority.None, false);
            }
        }
        var returnDeclaration = sig.Declaration();
        if (returnDeclaration != null)
        {
            var returnTypeNode = returnDeclaration.Type();
            if (returnTypeNode != null)
            {
                var source = GetTypeFromTypeNodeWorker(returnTypeNode);
                var target = GetReturnTypeOfSignatureWorker(context);
                InferTypes(inferenceContext._inferences, source, target, InferencePriority.None, false);
            }
        }
    }
}
