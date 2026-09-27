// ported from: internal/checker/checker.go:13905-14182
//   （checkPropertyAssignment / checkShorthandPropertyAssignment / isInPropertyInitializerOrClassStaticBlock /
//    getNarrowedTypeOfSymbol / isReadonlyAssignmentDeclaration / isReadonlySymbol / checkObjectLiteralMethod /
//    checkExpressionForMutableLocation / getResolvedSymbol / getResolvedSymbolOrNil /
//    getReferencedValueOrAliasSymbol / getCannotFindNameDiagnosticForName）
//
// Go→C# 改写（本文件，逐条理由）：
// 1) Go 的无标签 `switch { case cond: … }` → C# `if/else if`（C# 的 switch 需要常量模式）。Go 在 case 体里的
//    `break`（跳出 switch）→ 该分支里把后继语句包在 `if (!skip)` 中，语义与"提前离开 case"逐行一致；
//    `getCannotFindNameDiagnosticForName` 的 `case "await": … fallthrough` → C# `goto default;`（同一 switch）。
// 2) `tryGetThisTypeAtEx` 之外，本文件命中 `artifacts/checker-method-collisions.md` 的未导出成员照写改后的名字：
//    `isReadonlySymbol` → `IsReadonlySymbolWorker`（本文件定义）、`getTypeOfSymbol` → `GetTypeOfSymbolWorker`、
//    `getResolvedSymbol` → `GetResolvedSymbolWorker`（本文件定义）、`getTypeOfPropertyOfType` →
//    `GetTypeOfPropertyOfTypeWorker`、`getPropertyOfType` → `GetPropertyOfTypeWorker`、`getContextualType` →
//    `GetContextualTypeWorker`。
// 3) 包级函数（Go 无接收者）→ 同一个类的 static 成员：`hasDotDotDotToken` → `HasDotDotDotToken`（utilities.go）、
//    `isTypeAssertion` → `IsTypeAssertion`（utilities.go）、`getFlowNodeOfNode` → `GetFlowNodeOfNode`（flow.go）、
//    `signatureHasRestParameter` → `SignatureHasRestParameter`（checker.go:17356）、`getBooleanLiteralValue` →
//    `GetBooleanLiteralValue`（checker.go:25762）、`getDeclarationModifierFlagsFromSymbol` →
//    `GetDeclarationModifierFlagsFromSymbol`（utilities.go）。
// 4) `slices.Index(fn.Parameters(), declaration)` → `Array.IndexOf(fn.Parameters(), declaration)`。
// 5) `symbol.ValueDeclaration`/`node.Parent` 等在 Go 里可能为 nil 的字段：C# 侧用 `!` 表达 Go 的非 nil 不变量
//    （Go 里紧接着就解引用）。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.JsNum;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;

public sealed partial class Checker
{
    /// <summary>Go: <c>func (c *Checker) checkPropertyAssignment(node *ast.Node, checkMode CheckMode) *Type</c>。</summary>
    internal Type CheckPropertyAssignment(Node node, CheckMode checkMode)
    {
        // Do not use hasDynamicName here, because that returns false for well known symbols.
        // We want to perform checkComputedPropertyName for all computed properties, including
        // well known symbols.
        if (IsComputedPropertyName(node.Name()!))
        {
            CheckComputedPropertyName(node.Name()!);
        }

        var initializerType = CheckExpressionForMutableLocation(node.Initializer()!, checkMode);
        if (node.Type() is not null)
        {
            var t = GetTypeFromTypeNodeWorker(node.Type()!);
            CheckTypeAssignableToAndOptionallyElaborate(initializerType, t, node, node.Initializer()!, null /*headMessage*/, null);
            return t;
        }

        return initializerType;
    }

    /// <summary>Go: <c>func (c *Checker) checkShorthandPropertyAssignment(node *ast.Node, inDestructuringPattern bool, checkMode CheckMode) *Type</c>。</summary>
    internal Type CheckShorthandPropertyAssignment(Node node, bool inDestructuringPattern, CheckMode checkMode)
    {
        Node? expr = null;
        if (!inDestructuringPattern)
        {
            expr = node.AsShorthandPropertyAssignment()!.ObjectAssignmentInitializer;
        }

        expr ??= node.Name();
        var expressionType = CheckExpressionForMutableLocation(expr!, checkMode);
        if (node.Type() is not null)
        {
            var t = GetTypeFromTypeNodeWorker(node.Type()!);
            CheckTypeAssignableToAndOptionallyElaborate(expressionType, t, node, expr, null /*headMessage*/, null);
            return t;
        }

        return expressionType;
    }

    /// <summary>Go: <c>func (c *Checker) isInPropertyInitializerOrClassStaticBlock(node *ast.Node, ignoreArrowFunctions bool) bool</c>。</summary>
    internal bool IsInPropertyInitializerOrClassStaticBlock(Node node, bool ignoreArrowFunctions)
    {
        return FindAncestorOrQuit(node, node2 =>
        {
            switch (node2.Kind)
            {
                case Kind.PropertyDeclaration:
                case Kind.ClassStaticBlockDeclaration:
                    return FindAncestorResult.True;
                case Kind.TypeQuery:
                case Kind.JsxClosingElement:
                    return FindAncestorResult.Quit;
                case Kind.ArrowFunction:
                    return IfElse(ignoreArrowFunctions, FindAncestorResult.False, FindAncestorResult.Quit);
                case Kind.Block:
                    return IfElse(
                        IsFunctionLikeDeclaration(node2.Parent!) && node2.Parent!.Kind != Kind.ArrowFunction,
                        FindAncestorResult.Quit,
                        FindAncestorResult.False);
                default:
                    return FindAncestorResult.False;
            }
        }) is not null;
    }

    /// <summary>Go: <c>func (c *Checker) getNarrowedTypeOfSymbol(symbol *ast.Symbol, location *ast.Node) *Type</c>。</summary>
    internal Type GetNarrowedTypeOfSymbol(Symbol symbol, Node location)
    {
        var t = GetTypeOfSymbolWorker(symbol);
        var declaration = symbol.ValueDeclaration;
        if (declaration is not null)
        {
            // If we have a non-rest binding element with no initializer declared as a const variable or a const-like
            // parameter (a parameter for which there are no assignments in the function body), and if the parent type
            // for the destructuring is a union type, one or more of the binding elements may represent discriminant
            // properties, and we want the effects of conditional checks on such discriminants to affect the types of
            // other binding elements from the same destructuring. Consider:
            //
            //   type Action =
            //       | { kind: 'A', payload: number }
            //       | { kind: 'B', payload: string };
            //
            //   function f({ kind, payload }: Action) {
            //       if (kind === 'A') {
            //           payload.toFixed();
            //       }
            //       if (kind === 'B') {
            //           payload.toUpperCase();
            //       }
            //   }
            //
            // Above, we want the conditional checks on 'kind' to affect the type of 'payload'. To facilitate this, we use
            // the binding pattern AST instance for '{ kind, payload }' as a pseudo-reference and narrow this reference
            // as if it occurred in the specified location. We then recompute the narrowed binding element type by
            // destructuring from the narrowed parent type.
            if (IsBindingElement(declaration) && declaration.Initializer() is null && !HasDotDotDotToken(declaration) && declaration.Parent!.Elements()!.Length >= 2)
            {
                // Go: case 体里的 `break` 跳出 switch（= 跳过本 case 的剩余语句）
                var rootDeclaration = GetRootDeclaration(declaration);
                var rootInitializer = rootDeclaration!.Initializer();

                // Avoid declaration circularity without blocking binding defaults or nested callbacks.
                var skip = rootInitializer is not null
                    && IsNodeDescendantOf(location, rootInitializer)
                    && GetControlFlowContainer(declaration) == GetControlFlowContainer(location);
                if (!skip)
                {
                    var parent = declaration.Parent!.Parent!;
                    if ((IsVariableDeclaration(rootDeclaration) && (GetCombinedNodeFlagsCached(rootDeclaration) & NodeFlags.Constant) != 0)
                        || IsParameterDeclaration(rootDeclaration))
                    {
                        var links = _nodeLinks.Get(parent);
                        if ((links._flags & NodeCheckFlags.InCheckIdentifier) == 0)
                        {
                            links._flags |= NodeCheckFlags.InCheckIdentifier;
                            var parentType = GetTypeForBindingElementParent(parent, CheckMode.Normal);
                            Type? parentTypeConstraint = null;
                            if (parentType is not null)
                            {
                                parentTypeConstraint = MapType(parentType, GetBaseConstraintOrType);
                            }

                            // Guard parent-type resolution only; flow analysis should allow re-entrant narrowing
                            links._flags &= ~NodeCheckFlags.InCheckIdentifier;
                            if (parentTypeConstraint is not null
                                && (parentTypeConstraint._flags & TypeFlags.Union) != 0
                                && !(IsParameterDeclaration(rootDeclaration) && IsSomeSymbolAssigned(rootDeclaration)))
                            {
                                var pattern = declaration.Parent!;
                                var narrowedType = GetFlowTypeOfReferenceEx(pattern, parentTypeConstraint, parentTypeConstraint, null /*flowContainer*/, GetFlowNodeOfNode(location));
                                if ((narrowedType._flags & TypeFlags.Never) != 0)
                                {
                                    t = _neverType!;
                                }
                                else
                                {
                                    // Destructurings are validated against the parent type elsewhere. Here we disable tuple bounds
                                    // checks because the narrowed type may have lower arity than the full parent type. For example,
                                    // for the declaration [x, y]: [1, 2] | [3], we may have narrowed the parent type to just [3].
                                    t = GetBindingElementTypeFromParentType(declaration, narrowedType, true /*noTupleBoundsCheck*/);
                                }
                            }
                        }
                    }
                }
            }
            // If we have a const-like parameter with no type annotation or initializer, and if the parameter is contextually
            // typed by a signature with a single rest parameter of a union of tuple types, one or more of the parameters may
            // represent discriminant tuple elements, and we want the effects of conditional checks on such discriminants to
            // affect the types of other parameters in the same parameter list. Consider:
            //
            //   type Action = [kind: 'A', payload: number] | [kind: 'B', payload: string];
            //
            //   const f: (...args: Action) => void = (kind, payload) => {
            //       if (kind === 'A') {
            //           payload.toFixed();
            //       }
            //       if (kind === 'B') {
            //           payload.toUpperCase();
            //       }
            //   }
            //
            // Above, we want the conditional checks on 'kind' to affect the type of 'payload'. To facilitate this, we use
            // the arrow function AST node for '(kind, payload) => ...' as a pseudo-reference and narrow this reference as
            // if it occurred in the specified location. We then recompute the narrowed parameter type by indexing into the
            // narrowed tuple type.
            else if (IsParameterDeclaration(declaration) && declaration.Type() is null && declaration.Initializer() is null && !HasDotDotDotToken(declaration))
            {
                var fn = declaration.Parent!;
                if (fn.Parameters()!.Length >= 2 && IsContextSensitiveFunctionOrObjectLiteralMethod(fn))
                {
                    var contextualSignature = GetContextualSignature(fn);
                    if (contextualSignature is not null && (contextualSignature._parameters ?? []).Length == 1 && SignatureHasRestParameter(contextualSignature))
                    {
                        TypeMapper? mapper = null;
                        var context = GetInferenceContext(fn);
                        if (context is not null)
                        {
                            mapper = context._nonFixingMapper;
                        }

                        var restType = GetReducedApparentType(InstantiateType(GetTypeOfSymbolWorker(contextualSignature._parameters![0]), mapper));
                        if ((restType._flags & TypeFlags.Union) != 0 && EveryType(restType, IsTupleType) && !Some(fn.Parameters(), IsSomeSymbolAssigned))
                        {
                            var narrowedType = GetFlowTypeOfReferenceEx(fn, restType, restType, null /*flowContainer*/, GetFlowNodeOfNode(location));
                            var index = Array.IndexOf(fn.Parameters()!, declaration) - IfElse(GetThisParameter(fn) is not null, 1, 0);
                            t = GetIndexedAccessType(narrowedType, GetNumberLiteralType(new Number(index)));
                        }
                    }
                }
            }
        }

        return t;
    }

    /// <summary>Go: <c>func (c *Checker) isReadonlyAssignmentDeclaration(node *ast.Node) bool</c>。</summary>
    internal bool IsReadonlyAssignmentDeclaration(Node node)
    {
        if (!IsCallExpression(node))
        {
            return false;
        }

        var propertyDescriptorType = CheckExpressionCached(node.Arguments()![2]);
        var valueType = GetTypeOfPropertyOfTypeWorker(propertyDescriptorType, "value");
        if (valueType is not null)
        {
            var writableProp = GetPropertyOfTypeWorker(propertyDescriptorType, "writable");
            if (writableProp is not null)
            {
                Type writableType;
                if (writableProp.ValueDeclaration is not null && IsPropertyAssignment(writableProp.ValueDeclaration))
                {
                    writableType = CheckExpression(writableProp.ValueDeclaration.Initializer()!);
                }
                else
                {
                    writableType = GetTypeOfSymbolWorker(writableProp);
                }

                return (writableType._flags & TypeFlags.BooleanLiteral) != 0 && !GetBooleanLiteralValue(writableType);
            }

            return true;
        }

        return GetTypeOfPropertyOfTypeWorker(propertyDescriptorType, "set") is null;
    }

    /// <summary>Go: <c>func (c *Checker) isReadonlySymbol(symbol *ast.Symbol) bool</c>（C# 名加 Worker 后缀，见文件头 2）。</summary>
    internal bool IsReadonlySymbolWorker(Symbol symbol)
    {
        // The following symbols are considered read-only:
        // Properties with a 'readonly' modifier
        // Variables declared with 'const'
        // Get accessors without matching set accessors
        // Enum members
        // Object.defineProperty assignments with writable false or no setter
        // Unions and intersections of the above (unions and intersections eagerly set isReadonly on creation)
        return (symbol.CheckFlags & CheckFlags.Readonly) != 0
            || ((symbol.Flags & SymbolFlags.Property) != 0 && (GetDeclarationModifierFlagsFromSymbolWorker(symbol) & ModifierFlags.Readonly) != 0)
            || ((symbol.Flags & SymbolFlags.Variable) != 0 && (GetDeclarationNodeFlagsFromSymbol(symbol) & NodeFlags.Constant) != 0)
            || ((symbol.Flags & SymbolFlags.Accessor) != 0 && (symbol.Flags & SymbolFlags.SetAccessor) == 0)
            || (symbol.Flags & SymbolFlags.EnumMember) != 0
            || Some(symbol.Declarations, IsReadonlyAssignmentDeclaration);
    }

    /// <summary>Go: <c>func (c *Checker) checkObjectLiteralMethod(node *ast.Node, checkMode CheckMode) *Type</c>。</summary>
    internal Type CheckObjectLiteralMethod(Node node, CheckMode checkMode)
    {
        // Grammar checking
        CheckGrammarMethod(node);

        // Do not use hasDynamicName here, because that returns false for well known symbols.
        // We want to perform checkComputedPropertyName for all computed properties, including
        // well known symbols.
        if (IsComputedPropertyName(node.Name()!))
        {
            CheckComputedPropertyName(node.Name()!);
        }

        var uninstantiatedType = CheckFunctionExpressionOrObjectLiteralMethod(node, checkMode);
        return InstantiateTypeWithSingleGenericCallSignature(node, uninstantiatedType, checkMode);
    }

    /// <summary>Go: <c>func (c *Checker) checkExpressionForMutableLocation(node *ast.Node, checkMode CheckMode) *Type</c>。</summary>
    internal Type CheckExpressionForMutableLocation(Node node, CheckMode checkMode)
    {
        var t = CheckExpressionEx(node, checkMode);
        if (IsConstContext(node))
        {
            return GetRegularTypeOfLiteralType(t);
        }

        if (IsTypeAssertion(node))
        {
            return t;
        }

        return GetWidenedLiteralLikeTypeForContextualType(t, InstantiateContextualType(GetContextualTypeWorker(node, ContextFlags.None), node, ContextFlags.None));
    }

    /// <summary>Go: <c>func (c *Checker) getResolvedSymbol(node *ast.Node) *ast.Symbol</c>（C# 名加 Worker 后缀，见文件头 2）。</summary>
    internal Symbol GetResolvedSymbolWorker(Node node)
    {
        var links = _symbolNodeLinks.Get(node);
        if (links._resolvedSymbol is null)
        {
            Symbol? symbol = null;
            if (!NodeIsMissing(node))
            {
                symbol = _resolveName(
                    node,
                    node.Text(),
                    SymbolFlags.Value | SymbolFlags.ExportValue,
                    GetCannotFindNameDiagnosticForName(node),
                    !IsWriteOnlyAccess(node),
                    false /*excludeGlobals*/);
            }

            links._resolvedSymbol = OrElse(symbol, _unknownSymbol!);
        }

        return links._resolvedSymbol!;
    }

    /// <summary>Go: <c>func (c *Checker) getResolvedSymbolOrNil(node *ast.Node) *ast.Symbol</c>。</summary>
    internal Symbol? GetResolvedSymbolOrNil(Node node)
    {
        return _symbolNodeLinks.Get(node)._resolvedSymbol;
    }

    /// <summary>Go: <c>func (c *Checker) getReferencedValueOrAliasSymbol(reference *ast.Node) *ast.Symbol</c>。</summary>
    internal Symbol? GetReferencedValueOrAliasSymbol(Node reference)
    {
        var resolvedSymbol = _symbolNodeLinks.Get(reference)._resolvedSymbol;
        if (resolvedSymbol is not null && resolvedSymbol != _unknownSymbol)
        {
            return resolvedSymbol;
        }

        return _resolveName(reference, reference.Text(), SymbolFlags.Value | SymbolFlags.ExportValue | SymbolFlags.Alias, null, false /*isUse*/, false /*excludeGlobals*/);
    }

    /// <summary>Go: <c>func (c *Checker) getCannotFindNameDiagnosticForName(node *ast.Node) *diagnostics.Message</c>。</summary>
    internal Message GetCannotFindNameDiagnosticForName(Node node)
    {
        switch (node.Text())
        {
            case "document":
            case "console":
                return Cannot_find_name_0_Do_you_need_to_change_your_target_library_Try_changing_the_lib_compiler_option_to_include_dom;
            case "$":
                return IfElse(
                    _compilerOptions.UsesWildcardTypes(),
                    Cannot_find_name_0_Do_you_need_to_install_type_definitions_for_jQuery_Try_npm_i_save_dev_types_Slashjquery,
                    Cannot_find_name_0_Do_you_need_to_install_type_definitions_for_jQuery_Try_npm_i_save_dev_types_Slashjquery_and_then_add_jquery_to_the_types_field_in_your_tsconfig);
            case "beforeEach":
            case "describe":
            case "suite":
            case "it":
            case "test":
                return IfElse(
                    _compilerOptions.UsesWildcardTypes(),
                    Cannot_find_name_0_Do_you_need_to_install_type_definitions_for_a_test_runner_Try_npm_i_save_dev_types_Slashjest_or_npm_i_save_dev_types_Slashmocha,
                    Cannot_find_name_0_Do_you_need_to_install_type_definitions_for_a_test_runner_Try_npm_i_save_dev_types_Slashjest_or_npm_i_save_dev_types_Slashmocha_and_then_add_jest_or_mocha_to_the_types_field_in_your_tsconfig);
            case "process":
            case "require":
            case "Buffer":
            case "module":
            case "NodeJS":
                return IfElse(
                    _compilerOptions.UsesWildcardTypes(),
                    Cannot_find_name_0_Do_you_need_to_install_type_definitions_for_node_Try_npm_i_save_dev_types_Slashnode,
                    Cannot_find_name_0_Do_you_need_to_install_type_definitions_for_node_Try_npm_i_save_dev_types_Slashnode_and_then_add_node_to_the_types_field_in_your_tsconfig);
            case "Bun":
                return IfElse(
                    _compilerOptions.UsesWildcardTypes(),
                    Cannot_find_name_0_Do_you_need_to_install_type_definitions_for_Bun_Try_npm_i_save_dev_types_Slashbun,
                    Cannot_find_name_0_Do_you_need_to_install_type_definitions_for_Bun_Try_npm_i_save_dev_types_Slashbun_and_then_add_bun_to_the_types_field_in_your_tsconfig);
            case "Map":
            case "Set":
            case "Promise":
            case "Symbol":
            case "WeakMap":
            case "WeakSet":
            case "Iterator":
            case "AsyncIterator":
            case "SharedArrayBuffer":
            case "Atomics":
            case "AsyncIterable":
            case "AsyncIterableIterator":
            case "AsyncGenerator":
            case "AsyncGeneratorFunction":
            case "BigInt":
            case "Reflect":
            case "BigInt64Array":
            case "BigUint64Array":
                return Cannot_find_name_0_Do_you_need_to_change_your_target_library_Try_changing_the_lib_compiler_option_to_1_or_later;
            case "await":
                if (IsCallExpression(node.Parent!))
                {
                    return Cannot_find_name_0_Did_you_mean_to_write_this_in_an_async_function;
                }

                // Go: fallthrough → default
                goto default;
            default:
                if (node.Parent!.Kind == Kind.ShorthandPropertyAssignment)
                {
                    return No_value_exists_in_scope_for_the_shorthand_property_0_Either_declare_one_or_provide_an_initializer;
                }

                return Cannot_find_name_0;
        }
    }
}
