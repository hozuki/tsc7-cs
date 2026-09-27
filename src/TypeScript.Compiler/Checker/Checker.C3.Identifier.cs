// ported from: internal/checker/checker.go:11150-11441（yield/synthetic 表达式与标识符检查：
//   checkYieldExpression、getYieldedTypeOfYieldExpression、checkSyntheticExpression、checkIdentifier、
//   isSameScopedBindingElement、removeOptionalityFromDeclaredType、parameterInitializerContainsUndefined、
//   isInAmbientOrTypeNode）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) 局部 `var iterationTypes IterationTypes` 是 Go 的结构体零值（三个字段全 nil）→ C# 的
//    `new IterationTypes()`（`IterationTypes` 在 C# 是 class，Checker.Keys.cs:1063）。
// 2) `node.AsSyntheticExpression().Type.(*Type)`（Go 的 `any` + 类型断言）→ `(Type)node.AsSyntheticExpression()!.Type!`
//    （`SyntheticExpression.Type` 在 C# 是 `object?`）。
// 3) Worker 改名（artifacts/checker-method-collisions.md）：`getTypeOfSymbol` → `GetTypeOfSymbolWorker`、
//    `getResolvedSymbol` → `GetResolvedSymbolWorker`、`getBaseTypeOfLiteralType` → `GetBaseTypeOfLiteralTypeWorker`、
//    `getContextualType` → `GetContextualTypeWorker`、`isReadonlySymbol` → `IsReadonlySymbolWorker`（后两者见别处）。
// 4) Go 的 `slices.Contains(c.contextualBindingPatterns, x)` → `_contextualBindingPatterns.Contains(x)`
//    （C1 已落成 `List<Node>`）。
// 5) `debug.Assert` → `Debug.Assert`；`core.OrElse`/`core.IfElse` → `OrElse`/`IfElse`。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;

public sealed partial class Checker
{
    internal Type CheckYieldExpression(Node node)
    {
        CheckGrammarYieldExpression(node);
        // Always check the operand so its identifiers are resolved even when the yield is
        // outside a generator, keeping diagnostics stable regardless of traversal order.
        Type yieldExpressionType;
        if (node.Expression() != null)
        {
            yieldExpressionType = CheckExpression(node.Expression()!);
        }
        else
        {
            yieldExpressionType = _undefinedWideningType;
        }
        var fn = GetContainingFunction(node);
        if (fn == null)
        {
            return _anyType;
        }
        var functionFlags = GetFunctionFlags(fn);
        if ((functionFlags & FunctionFlags.Generator) == 0)
        {
            // If the user's code is syntactically correct, the func should always have a star. After all, we are in a yield context.
            return _anyType;
        }
        var isAsync = (functionFlags & FunctionFlags.Async) != 0;
        if (node.AsYieldExpression()!.AsteriskToken != null)
        {
            // Async generator functions prior to ES2018 require the __await, __asyncDelegator,
            // and __asyncValues helpers
            if (isAsync && _languageVersion < LanguageFeatureMinimumTarget.AsyncGenerators)
            {
                CheckExternalEmitHelpers(node, ExternalEmitHelpers.AsyncDelegatorIncludes);
            }
        }
        // There is no point in doing an assignability check if the function
        // has no explicit return type because the return type is directly computed
        // from the yield expressions.
        var returnType = GetReturnTypeFromAnnotation(fn);
        if (returnType != null && (returnType._flags & TypeFlags.Union) != 0)
        {
            returnType = FilterType(returnType, t => CheckGeneratorInstantiationAssignabilityToReturnType(t, functionFlags, null /*errorNode*/));
        }
        var iterationTypes = new IterationTypes();
        if (returnType != null)
        {
            iterationTypes = GetIterationTypesOfGeneratorFunctionReturnType(returnType, isAsync);
        }
        var signatureYieldType = OrElse(iterationTypes._yieldType, _anyType)!;
        var signatureNextType = OrElse(iterationTypes._nextType, _anyType)!;
        var yieldedType = GetYieldedTypeOfYieldExpression(node, yieldExpressionType, signatureNextType, isAsync);
        if (returnType != null && yieldedType != null)
        {
            CheckTypeAssignableToAndOptionallyElaborate(yieldedType, signatureYieldType, OrElse(node.Expression(), node)!, node.Expression(), null, null);
        }
        if (node.AsYieldExpression()!.AsteriskToken != null)
        {
            var use = IfElse(isAsync, IterationUse.AsyncYieldStar, IterationUse.YieldStar);
            return OrElse(GetIterationTypeOfIterable(use, IterationTypeKind.Return, yieldExpressionType, node.Expression()), _anyType)!;
        }
        if (returnType != null)
        {
            return OrElse(GetIterationTypeOfGeneratorFunctionReturnType(IterationTypeKind.Next, returnType, isAsync), _anyType)!;
        }
        var t = GetContextualIterationType(IterationTypeKind.Next, fn);
        if (t == null)
        {
            t = _anyType;
            if (_noImplicitAny && !ExpressionResultIsUnused(node))
            {
                var contextualType = GetContextualTypeWorker(node, ContextFlags.None);
                if (contextualType == null || IsTypeAny(contextualType))
                {
                    Error(node, Diagnostics.X_yield_expression_implicitly_results_in_an_any_type_because_its_containing_generator_lacks_a_return_type_annotation);
                }
            }
        }
        return t;
    }

    internal Type GetYieldedTypeOfYieldExpression(Node node, Type expressionType, Type sentType, bool isAsync)
    {
        var errorNode = OrElse(node.Expression(), node)!;
        var isYieldStar = node.AsYieldExpression()!.AsteriskToken != null;
        // A `yield*` expression effectively yields everything that its operand yields
        var yieldedType = expressionType;
        if (isYieldStar)
        {
            yieldedType = CheckIteratedTypeOrElementType(IfElse(isAsync, IterationUse.AsyncYieldStar, IterationUse.YieldStar), expressionType, sentType, errorNode);
        }
        if (!isAsync)
        {
            return yieldedType;
        }
        // Go 的 getAwaitedTypeEx 只在循环 await 报错路径返回 nil（Go 里 tsc/internal/checker/checker.go:31661-31667）；
        // 调用点（checkYieldExpression）已经按 Go 判空，这里断言与 Go 的 `*Type` 契约一致。
        return GetAwaitedTypeEx(yieldedType, errorNode, IfElse(isYieldStar,
            Diagnostics.Type_of_iterated_elements_of_a_yield_Asterisk_operand_must_either_be_a_valid_promise_or_must_not_contain_a_callable_then_member,
            Diagnostics.Type_of_yield_operand_in_an_async_generator_must_either_be_a_valid_promise_or_must_not_contain_a_callable_then_member))!;
    }

    internal Type CheckSyntheticExpression(Node node)
    {
        var data = node.AsSyntheticExpression()!;
        var t = (Type)data.Type!;
        if (data.IsSpread)
        {
            return GetIndexedAccessType(t, _numberType);
        }
        return t;
    }

    internal Type CheckIdentifier(Node node, CheckMode checkMode)
    {
        if (IsThisInTypeQuery(node))
        {
            return CheckThisExpression(node);
        }
        var symbol = GetResolvedSymbolWorker(node);
        if (symbol == _unknownSymbol)
        {
            return _errorType;
        }
        if (symbol == _argumentsSymbol)
        {
            if (IsInPropertyInitializerOrClassStaticBlock(node, true /*ignoreArrowFunctions*/))
            {
                Error(node, Diagnostics.X_arguments_cannot_be_referenced_in_property_initializers_or_class_static_initialization_blocks);
                return _errorType;
            }
            return GetTypeOfSymbolWorker(symbol);
        }
        if (ShouldMarkIdentifierAliasReferenced(node))
        {
            MarkLinkedReferences(node, ReferenceHint.Identifier, null /*propSymbol*/, null /*parentType*/);
        }
        // Go: `getExportSymbolOfValueSymbolIfExported` 对非 nil 输入返回非 nil（checker.go:14609-14614），
        // 且 `resolveAliasWithDeprecationCheck`（checker.go:16628）直接解引用 symbol。
        var localOrExportSymbol = GetExportSymbolOfValueSymbolIfExported(symbol)!;
        var targetSymbol = ResolveAliasWithDeprecationCheck(localOrExportSymbol, node);
        if (targetSymbol.Declarations != null && targetSymbol.Declarations.Length != 0 && IsDeprecatedSymbol(targetSymbol) && IsUncalledFunctionReference(node, targetSymbol))
        {
            AddDeprecatedSuggestion(node, targetSymbol.Declarations, node.Text());
        }
        var declaration = localOrExportSymbol.ValueDeclaration;
        var immediateDeclaration = declaration;
        // If the identifier is declared in a binding pattern for which we're currently computing the implied type and the
        // reference occurs with the same binding pattern, return the non-inferrable any type. This for example occurs in
        // 'const [a, b = a + 1] = [2]' when we're computing the contextual type for the array literal '[2]'.
        if (declaration != null && declaration.Kind == Kind.BindingElement && _contextualBindingPatterns.Contains(declaration.Parent!) &&
            FindAncestor(node, parent => parent == declaration.Parent) != null)
        {
            return _nonInferrableAnyType;
        }
        var t = GetNarrowedTypeOfSymbol(localOrExportSymbol, node);
        var assignmentKind = GetAssignmentTargetKind(node);
        if (assignmentKind != AssignmentKind.None)
        {
            if ((localOrExportSymbol.Flags & SymbolFlags.Variable) == 0 && !(IsInJSFile(node) && (localOrExportSymbol.Flags & SymbolFlags.ValueModule) != 0))
            {
                Message assignmentError;
                if ((localOrExportSymbol.Flags & SymbolFlags.Enum) != 0)
                {
                    assignmentError = Diagnostics.Cannot_assign_to_0_because_it_is_an_enum;
                }
                else if ((localOrExportSymbol.Flags & SymbolFlags.Class) != 0)
                {
                    assignmentError = Diagnostics.Cannot_assign_to_0_because_it_is_a_class;
                }
                else if ((localOrExportSymbol.Flags & SymbolFlags.Module) != 0)
                {
                    assignmentError = Diagnostics.Cannot_assign_to_0_because_it_is_a_namespace;
                }
                else if ((localOrExportSymbol.Flags & SymbolFlags.Function) != 0)
                {
                    assignmentError = Diagnostics.Cannot_assign_to_0_because_it_is_a_function;
                }
                else if ((localOrExportSymbol.Flags & SymbolFlags.Alias) != 0)
                {
                    assignmentError = Diagnostics.Cannot_assign_to_0_because_it_is_an_import;
                }
                else
                {
                    assignmentError = Diagnostics.Cannot_assign_to_0_because_it_is_not_a_variable;
                }
                Error(node, assignmentError, SymbolToStringWorker(symbol));
                return _errorType;
            }
            if (IsReadonlySymbolWorker(localOrExportSymbol))
            {
                if ((localOrExportSymbol.Flags & SymbolFlags.Variable) != 0)
                {
                    Error(node, Diagnostics.Cannot_assign_to_0_because_it_is_a_constant, SymbolToStringWorker(symbol));
                }
                else
                {
                    Error(node, Diagnostics.Cannot_assign_to_0_because_it_is_a_read_only_property, SymbolToStringWorker(symbol));
                }
                return _errorType;
            }
        }
        var isAlias = (localOrExportSymbol.Flags & SymbolFlags.Alias) != 0;
        // We only narrow variables and parameters occurring in a non-assignment position. For all other
        // entities we simply return the declared type.
        if ((localOrExportSymbol.Flags & SymbolFlags.Variable) != 0)
        {
            if (assignmentKind == AssignmentKind.Definite)
            {
                if (IsInCompoundLikeAssignment(node))
                {
                    return GetBaseTypeOfLiteralTypeWorker(t);
                }
                return t;
            }
        }
        else if (isAlias)
        {
            declaration = GetDeclarationOfAliasSymbol(symbol);
        }
        else
        {
            return t;
        }
        if (declaration == null)
        {
            return t;
        }
        t = GetNarrowableTypeForReference(t, node, checkMode);
        // The declaration container is the innermost function that encloses the declaration of the variable
        // or parameter. The flow container is the innermost function starting with which we analyze the control
        // flow graph to determine the control flow based type.
        var isParameter = GetRootDeclaration(declaration)!.Kind == Kind.Parameter;
        var declarationContainer = GetControlFlowContainer(declaration);
        var flowContainer = GetControlFlowContainer(node);
        var isOuterVariable = flowContainer != declarationContainer;
        var isSpreadDestructuringAssignmentTarget = node.Parent != null && node.Parent.Parent != null && IsSpreadAssignment(node.Parent) && IsDestructuringAssignmentTarget(node.Parent.Parent);
        var isModuleExports = (symbol.Flags & SymbolFlags.ModuleExports) != 0;
        var typeIsAutomatic = t == _autoType || t == _autoArrayType;
        var isAutomaticTypeInNonNull = typeIsAutomatic && node.Parent!.Kind == Kind.NonNullExpression;
        // When the control flow originates in a function expression, arrow function, method, or accessor, and
        // we are referencing a closed-over const variable or parameter or mutable local variable past its last
        // assignment, we extend the origin of the control flow analysis to include the immediately enclosing
        // control flow container.
        while (flowContainer != declarationContainer &&
            (IsFunctionExpressionOrArrowFunction(flowContainer!) || IsObjectLiteralOrClassExpressionMethodOrAccessor(flowContainer)) &&
            (IsConstantVariable(localOrExportSymbol) && t != _autoArrayType || IsParameterOrMutableLocalVariable(localOrExportSymbol) && IsPastLastAssignment(localOrExportSymbol, node)))
        {
            flowContainer = GetControlFlowContainer(flowContainer!);
        }
        // We only look for uninitialized variables in strict null checking mode, and only when we can analyze
        // the entire control flow graph from the variable's declaration (i.e. when the flow container and
        // declaration container are the same).
        var isNeverInitialized = immediateDeclaration != null && IsVariableDeclaration(immediateDeclaration) && !IsForInOrOfStatement(immediateDeclaration.Parent!.Parent!) &&
            immediateDeclaration.Initializer() == null && immediateDeclaration.AsVariableDeclaration()!.ExclamationToken == null &&
            IsMutableLocalVariableDeclaration(immediateDeclaration) && !IsSymbolAssignedDefinitely(symbol);
        var assumeInitialized = isParameter ||
            isAlias ||
            (isOuterVariable && !isNeverInitialized) ||
            isSpreadDestructuringAssignmentTarget ||
            isModuleExports ||
            IsSameScopedBindingElement(node, declaration) ||
            t != _autoType && t != _autoArrayType && (!_strictNullChecks || (t._flags & (TypeFlags.AnyOrUnknown | TypeFlags.Void)) != 0 || IsInTypeQuery(node) || IsInAmbientOrTypeNode(node) || node.Parent!.Kind == Kind.ExportSpecifier) ||
            IsNonNullExpression(node.Parent!) ||
            IsVariableDeclaration(declaration) && declaration.AsVariableDeclaration()!.ExclamationToken != null ||
            (declaration.Flags & NodeFlags.Ambient) != 0;
        Type initialType;
        if (isAutomaticTypeInNonNull)
        {
            initialType = _undefinedType;
        }
        else if (assumeInitialized && isParameter)
        {
            initialType = RemoveOptionalityFromDeclaredType(t, declaration);
        }
        else if (assumeInitialized)
        {
            initialType = t;
        }
        else if (typeIsAutomatic)
        {
            initialType = _undefinedType;
        }
        else
        {
            initialType = GetOptionalType(t, false /*isProperty*/);
        }
        Type flowType;
        if (isAutomaticTypeInNonNull)
        {
            flowType = GetNonNullableType(GetFlowTypeOfReferenceEx(node, t, initialType, flowContainer, null));
        }
        else
        {
            flowType = GetFlowTypeOfReferenceEx(node, t, initialType, flowContainer, null);
        }
        // A variable is considered uninitialized when it is possible to analyze the entire control flow graph
        // from declaration to use, and when the variable's declared type doesn't include undefined but the
        // control flow based type does include undefined.
        if (!IsEvolvingArrayOperationTarget(node) && (t == _autoType || t == _autoArrayType))
        {
            if (flowType == _autoType || flowType == _autoArrayType)
            {
                if (_noImplicitAny)
                {
                    Error(GetNameOfDeclaration(declaration), Diagnostics.Variable_0_implicitly_has_type_1_in_some_locations_where_its_type_cannot_be_determined, SymbolToStringWorker(symbol), TypeToString(flowType));
                    Error(node, Diagnostics.Variable_0_implicitly_has_an_1_type, SymbolToStringWorker(symbol), TypeToString(flowType));
                }
                return ConvertAutoToAny(flowType);
            }
        }
        else if (!assumeInitialized && !ContainsUndefinedType(t) && ContainsUndefinedType(flowType))
        {
            Error(node, Diagnostics.Variable_0_is_used_before_being_assigned, SymbolToStringWorker(symbol));
            // Return the declared type to reduce follow-on errors
            return t;
        }
        if (assignmentKind != AssignmentKind.None)
        {
            // Identifier is target of a compound assignment
            return GetBaseTypeOfLiteralTypeWorker(flowType);
        }
        return flowType;
    }

    internal bool IsSameScopedBindingElement(Node node, Node declaration)
    {
        if (IsBindingElement(declaration))
        {
            var bindingElement = FindAncestor(node, IsBindingElement);
            return bindingElement != null && GetRootDeclaration(bindingElement) == GetRootDeclaration(declaration);
        }
        return false;
    }

    // Remove undefined from the annotated type of a parameter when there is an initializer (that doesn't include undefined)
    internal Type RemoveOptionalityFromDeclaredType(Type declaredType, Node declaration)
    {
        var removeUndefined = _strictNullChecks && IsParameterDeclaration(declaration) && declaration.Initializer() != null && HasTypeFacts(declaredType, TypeFacts.IsUndefined) && !ParameterInitializerContainsUndefined(declaration);
        if (removeUndefined)
        {
            return GetTypeWithFacts(declaredType, TypeFacts.NEUndefined);
        }
        return declaredType;
    }

    internal bool ParameterInitializerContainsUndefined(Node declaration)
    {
        var links = _nodeLinks.Get(declaration);
        if ((links._flags & NodeCheckFlags.InitializerIsUndefinedComputed) == 0)
        {
            if (!PushTypeResolution(declaration, TypeSystemPropertyName.InitializerIsUndefined))
            {
                ReportCircularityError(declaration.Symbol!);
                return true;
            }
            var containsUndefined = HasTypeFacts(CheckDeclarationInitializer(declaration, CheckMode.Normal, null), TypeFacts.IsUndefined);
            if (!PopTypeResolution())
            {
                ReportCircularityError(declaration.Symbol!);
                return true;
            }
            if ((links._flags & NodeCheckFlags.InitializerIsUndefinedComputed) == 0)
            {
                links._flags |= NodeCheckFlags.InitializerIsUndefinedComputed | IfElse(containsUndefined, NodeCheckFlags.InitializerIsUndefined, (NodeCheckFlags)0);
            }
        }
        return (links._flags & NodeCheckFlags.InitializerIsUndefined) != 0;
    }

    internal bool IsInAmbientOrTypeNode(Node node)
    {
        return (node.Flags & NodeFlags.Ambient) != 0 || FindAncestor(node, n =>
            IsInterfaceDeclaration(n) || IsTypeAliasDeclaration(n) || IsJSTypeAliasDeclaration(n) || IsTypeLiteralNode(n)) != null;
    }
}
