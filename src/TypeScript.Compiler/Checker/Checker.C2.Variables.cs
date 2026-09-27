// ported from: internal/checker/checker.go:5936-6262（checkVariableDeclarationList、checkVariableDeclaration、
//   checkVariableLikeDeclaration、errorNextVariableOrPropertyDeclarationMustHaveSameType、
//   checkVarDeclaredNamesNotShadowed、checkDecorators、checkDecorator）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) `c.error` → `Error`（返回 `Diagnostic`）；`c.TypeToString` → `TypeToString`（Go 的导出单参版本，printer.go:43）；
//    `c.symbolToString` → `SymbolToStringWorker`（artifacts/checker-method-collisions.md 的 Worker 改名）。
// 2) Go 的**函数类型字段**在 C# 是委托字段（docs/checker-plan.md §5.5）：`c.resolveName(...)` → `_resolveName(...)`
//    （Checker.Struct.cs:109）；同理本文件里所有 `c.getGlobalXxx` 是字段 → `_getGlobalXxx()`。
// 3) `nil` → `null`；`append(c.renamedBindingElementsInTypes, node)` → `_renamedBindingElementsInTypes.Add(node)`
//    （C# 侧字段是 `List<Node>`）；`panic("Identifier expected")` → `Debug.Fail(...)` +
//    `throw new System.Diagnostics.UnreachableException()`（Roslyn 不知道 `Debug.Fail` 不返回，先例 Checker.C3.Calls.cs:16）。
// 4) `checkDecorator` 的 Go `switch` 用了 `fallthrough`（KindPropertyDeclaration → KindParameter），C# 没有 fallthrough
//    → 把 fallthrough 那一支的目标赋值显式重复一次；两处 `default: panic(...)` → `Debug.Fail(...)` + throw。
// 5) 局部变量重名：`checkVarDeclaredNamesNotShadowed` 里 Go 的内层 `name`（`c.symbolToString(...)`）遮蔽了外层
//    `name *ast.Node`；C# 禁止同作用域重名（CS0136），内层改名 `symbolName`（语义不变）。
// 6) 可空数组：`CheckSourceElements(Node[] nodes)` 的参数不可空、`NodeList.Nodes` 是 `Node[]?`，故调用点写 `!`
//    （AST 不变量：变量声明列表必有 Declarations）；`Elements()`/`ModifierNodes()` 同理。
// 注：Go 5927-5934 的 `checkVariableStatement` 不在本窗口（由 5608-5935 分片定义，见报告）。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.Tracing;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Scanner.Scanner;

public sealed partial class Checker
{
    internal void CheckVariableDeclarationList(Node node)
    {
        var blockScopeKind = GetCombinedNodeFlags(node) & NodeFlags.BlockScoped;
        if ((blockScopeKind == NodeFlags.Using || blockScopeKind == NodeFlags.AwaitUsing) && _languageVersion < LanguageFeatureMinimumTarget.UsingAndAwaitUsing)
        {
            CheckExternalEmitHelpers(node, ExternalEmitHelpers.AddDisposableResourceAndDisposeResources);
        }
        CheckSourceElements(node.AsVariableDeclarationList()!.Declarations!.Nodes!);
    }

    internal void CheckVariableDeclaration(Node node)
    {
        // Go: if tr := c.tracer; tr != nil { defer tr.Push(...)() }
        var tr = _tracer;
        Action? pop = tr?.Push(Phase.Check, "checkVariableDeclaration", new Dictionary<string, object?>
        {
            ["kind"] = node.Kind,
            ["pos"] = node.Pos(),
            ["end"] = node.End(),
            ["path"] = GetSourceFileOfNode(node)!.FileName(),
        }, false);
        try
        {
            CheckGrammarVariableDeclaration(node.AsVariableDeclaration()!);
            CheckVariableLikeDeclaration(node);
        }
        finally
        {
            pop?.Invoke();
        }
    }

    // Check variable, parameter, or property declaration
    internal void CheckVariableLikeDeclaration(Node node)
    {
        CheckDecorators(node);
        var name = node.Name();
        if (name == null)
        {
            return; // Missing array binding elements have no name
        }
        var typeNode = node.Type();
        var initializer = node.Initializer();
        if (!IsBindingElement(node))
        {
            CheckSourceElement(typeNode);
        }
        // For a computed property, just check the initializer and exit
        // Do not use hasDynamicName here, because that returns false for well known symbols.
        // We want to perform checkComputedPropertyName for all computed properties, including
        // well known symbols.
        if (IsComputedPropertyName(name))
        {
            CheckComputedPropertyName(name);
            if (initializer != null)
            {
                CheckExpressionCached(initializer);
            }
        }
        if (IsBindingElement(node))
        {
            var propName = node.PropertyName();

            if (propName != null && IsPrivateIdentifier(propName))
            {
                GrammarErrorOnNode(propName, Diagnostics.Private_identifiers_cannot_be_used_in_destructuring_patterns);
            }

            if (propName != null && IsIdentifier(node.Name()!) && IsPartOfParameterDeclaration(node) && NodeIsMissing(GetContainingFunction(node)!.Body()))
            {
                // type F = ({a: string}) => void;
                //               ^^^^^^
                // variable renaming in function type notation is confusing,
                // so we forbid it even if noUnusedLocals is not enabled
                _renamedBindingElementsInTypes.Add(node);
                return;
            }
            if (IsObjectBindingPattern(node.Parent!) && HasDotDotDotToken(node) && _languageVersion < LanguageFeatureMinimumTarget.ObjectSpreadRest)
            {
                CheckExternalEmitHelpers(node, ExternalEmitHelpers.Rest);
            }
            // check computed properties inside property names of binding elements
            if (propName != null && IsComputedPropertyName(propName))
            {
                CheckComputedPropertyName(propName);
            }
            // check private/protected variable access
            var parent = node.Parent!.Parent!;
            var parentCheckMode = IfElse(HasDotDotDotToken(node), CheckMode.RestBindingElement, CheckMode.Normal);
            var parentType = GetTypeForBindingElementParent(parent, parentCheckMode);
            var propNameName = node.PropertyNameOrName();
            if (parentType != null && !IsBindingPattern(propNameName))
            {
                // Go: node.Name() 已在上方判过非 nil，PropertyNameOrName() 因此也非 nil。
                var exprType = GetLiteralTypeFromPropertyName(propNameName!);
                if (IsTypeUsableAsPropertyNameWorker(exprType))
                {
                    var nameText = GetPropertyNameFromTypeWorker(exprType);
                    var property = GetPropertyOfTypeWorker(parentType, nameText);
                    if (property != null)
                    {
                        MarkPropertyAsReferenced(property, null /*nodeForCheckWriteOnly*/, false /*isSelfTypeAccess*/);
                        // A destructuring is never a write-only reference.
                        CheckPropertyAccessibility(node, parent.Initializer() != null && parent.Initializer()!.Kind == Kind.SuperKeyword, false /*writing*/, parentType, property);
                    }
                }
            }
        }
        // For a binding pattern, check contained binding elements
        if (IsBindingPattern(name))
        {
            CheckSourceElements(name.Elements()!);
        }
        // For a parameter declaration with an initializer, error and exit if the containing function doesn't have a body
        if (initializer != null && IsPartOfParameterDeclaration(node) && NodeIsMissing(GetContainingFunction(node)!.Body()))
        {
            Error(node, Diagnostics.A_parameter_initializer_is_only_allowed_in_a_function_or_constructor_implementation);
            return;
        }
        // For a binding pattern, validate the initializer and exit
        if (IsBindingPattern(name))
        {
            if (IsInAmbientOrTypeNode(node))
            {
                return;
            }
            var needCheckInitializer = initializer != null && node.Parent!.Parent!.Kind != Kind.ForInStatement;
            var needCheckWidenedType = !Some(name.Elements(), n => n.Name() != null);
            if (needCheckInitializer || needCheckWidenedType)
            {
                // Don't validate for-in initializer as it is already an error
                var widenedType = GetWidenedTypeForVariableLikeDeclaration(node, false /*reportErrors*/);
                if (needCheckInitializer)
                {
                    var initializerType = CheckExpressionCached(initializer!);
                    if (_strictNullChecks && needCheckWidenedType)
                    {
                        CheckNonNullNonVoidType(initializerType, node);
                    }
                    else
                    {
                        CheckTypeAssignableToAndOptionallyElaborate(initializerType, GetWidenedTypeForVariableLikeDeclaration(node, false), node, initializer, null, null);
                    }
                }
                // check the binding pattern with empty elements
                if (needCheckWidenedType)
                {
                    if (IsArrayBindingPattern(name))
                    {
                        CheckIteratedTypeOrElementType(IterationUse.Destructuring, widenedType, _undefinedType, node);
                    }
                    else if (_strictNullChecks)
                    {
                        CheckNonNullNonVoidType(widenedType, node);
                    }
                }
            }
            return;
        }
        // For a commonjs `const x = require`, validate the alias and exit
        // Go: c.getSymbolOfDeclaration(node) 的返回值在 Go 里被直接解引用（声明必有符号）。
        var symbol = GetSymbolOfDeclaration(node)!;
        if ((symbol.Flags & SymbolFlags.Alias) != 0 && IsVariableDeclarationInitializedToRequire(node))
        {
            CheckAliasSymbol(node);
            return;
        }
        if (IsBigIntLiteral(name))
        {
            Error(name, Diagnostics.A_bigint_literal_cannot_be_used_as_a_property_name);
        }
        var t = ConvertAutoToAny(GetTypeOfSymbolWorker(symbol));
        if (node == symbol.ValueDeclaration)
        {
            // Node is the primary declaration of the symbol, just validate the initializer
            // Don't validate for-in initializer as it is already an error
            if (initializer != null && !IsForInStatement(node.Parent!.Parent!))
            {
                var initializerType = CheckExpressionCached(initializer);
                CheckTypeAssignableToAndOptionallyElaborate(initializerType, t, node, initializer, null /*headMessage*/, null);
                var blockScopeKind = GetCombinedNodeFlagsCached(node) & NodeFlags.BlockScoped;
                if (blockScopeKind == NodeFlags.AwaitUsing)
                {
                    var globalAsyncDisposableType = _getGlobalAsyncDisposableType();
                    var globalDisposableType = _getGlobalDisposableType();
                    if (globalAsyncDisposableType != _emptyObjectType && globalDisposableType != _emptyObjectType)
                    {
                        var optionalDisposableType = GetUnionTypeWorker([globalAsyncDisposableType, globalDisposableType, _nullType, _undefinedType]);
                        CheckTypeAssignableTo(WidenTypeForVariableLikeDeclaration(initializerType, node, false), optionalDisposableType, initializer,
                            Diagnostics.The_initializer_of_an_await_using_declaration_must_be_either_an_object_with_a_Symbol_asyncDispose_or_Symbol_dispose_method_or_be_null_or_undefined);
                    }
                }
                else if (blockScopeKind == NodeFlags.Using)
                {
                    var globalDisposableType = _getGlobalDisposableType();
                    if (globalDisposableType != _emptyObjectType)
                    {
                        var optionalDisposableType = GetUnionTypeWorker([globalDisposableType, _nullType, _undefinedType]);
                        CheckTypeAssignableTo(WidenTypeForVariableLikeDeclaration(initializerType, node, false), optionalDisposableType, initializer,
                            Diagnostics.The_initializer_of_a_using_declaration_must_be_either_an_object_with_a_Symbol_dispose_method_or_be_null_or_undefined);
                    }
                }
            }
            if ((symbol.Declarations?.Length ?? 0) > 1)
            {
                if (Some(symbol.Declarations, d => d != node && IsVariableLike(d) && !AreDeclarationFlagsIdentical(d, node)))
                {
                    Error(name, Diagnostics.All_declarations_of_0_must_have_identical_modifiers, DeclarationNameToString(name));
                }
            }
        }
        else
        {
            // Node is a secondary declaration, check that type is identical to primary declaration and check that
            // initializer is consistent with type associated with the node
            var declarationType = ConvertAutoToAny(GetWidenedTypeForVariableLikeDeclaration(node, false));
            if (!IsErrorType(t) && !IsErrorType(declarationType) && !IsTypeIdenticalTo(t, declarationType) && (symbol.Flags & SymbolFlags.Assignment) == 0)
            {
                ErrorNextVariableOrPropertyDeclarationMustHaveSameType(symbol.ValueDeclaration, t, node, declarationType);
            }
            if (initializer != null)
            {
                CheckTypeAssignableToAndOptionallyElaborate(CheckExpressionCached(initializer), declarationType, node, initializer, null /*headMessage*/, null);
            }
            if (symbol.ValueDeclaration != null && !AreDeclarationFlagsIdentical(node, symbol.ValueDeclaration))
            {
                Error(name, Diagnostics.All_declarations_of_0_must_have_identical_modifiers, DeclarationNameToString(name));
            }
        }
        if (!IsPropertyDeclaration(node) && !IsPropertySignatureDeclaration(node))
        {
            // We know we don't have a binding pattern or computed name here
            CheckExportsOnMergedDeclarations(node);
            if (IsVariableDeclaration(node) || IsBindingElement(node))
            {
                CheckVarDeclaredNamesNotShadowed(node);
            }
            CheckCollisionsForDeclarationName(node, node.Name());
        }
    }

    internal void ErrorNextVariableOrPropertyDeclarationMustHaveSameType(Node? firstDeclaration, Type firstType, Node nextDeclaration, Type nextType)
    {
        var nextDeclarationName = GetNameOfDeclaration(nextDeclaration);
        var message = IfElse(IsPropertyDeclaration(nextDeclaration) || IsPropertySignatureDeclaration(nextDeclaration),
            Diagnostics.Subsequent_property_declarations_must_have_the_same_type_Property_0_must_be_of_type_1_but_here_has_type_2,
            Diagnostics.Subsequent_variable_declarations_must_have_the_same_type_Variable_0_must_be_of_type_1_but_here_has_type_2);
        var declName = DeclarationNameToString(nextDeclarationName);
        var err = Error(nextDeclarationName, message, declName, TypeToString(firstType), TypeToString(nextType));
        if (firstDeclaration != null)
        {
            err.AddRelatedInfo(CreateDiagnosticForNode(firstDeclaration, Diagnostics.X_0_was_also_declared_here, declName));
        }
    }

    internal void CheckVarDeclaredNamesNotShadowed(Node node)
    {
        // - ScriptBody : StatementList
        // It is a Syntax Error if any element of the LexicallyDeclaredNames of StatementList
        // also occurs in the VarDeclaredNames of StatementList.

        // - Block : { StatementList }
        // It is a Syntax Error if any element of the LexicallyDeclaredNames of StatementList
        // also occurs in the VarDeclaredNames of StatementList.

        // Variable declarations are hoisted to the top of their function scope. They can shadow
        // block scoped declarations, which bind tighter. this will not be flagged as duplicate definition
        // by the binder as the declaration scope is different.
        // A non-initialized declaration is a no-op as the block declaration will resolve before the var
        // declaration. the problem is if the declaration has an initializer. this will act as a write to the
        // block declared value. this is fine for let, but not const.
        // Only consider declarations with initializers, uninitialized const declarations will not
        // step on a let/const variable.
        // Do not consider const and const declarations, as duplicate block-scoped declarations
        // are handled by the binder.
        // We are only looking for const declarations that step on let\const declarations from a
        // different scope. e.g.:
        //      {
        //          const x = 0; // localDeclarationSymbol obtained after name resolution will correspond to this declaration
        //          const x = 0; // symbol for this declaration will be 'symbol'
        //      }

        // skip block-scoped variables and parameters
        if ((GetCombinedNodeFlagsCached(node) & NodeFlags.BlockScoped) != 0 || IsPartOfParameterDeclaration(node))
        {
            return;
        }
        // NOTE: in ES6 spec initializer is required in variable declarations where name is binding pattern
        // so we'll always treat binding elements as initialized
        var symbol = GetSymbolOfDeclaration(node)!;
        var name = node.Name();
        if ((symbol.Flags & SymbolFlags.FunctionScopedVariable) != 0)
        {
            if (!IsIdentifier(name!))
            {
                Debug.Fail("Identifier expected");
                throw new System.Diagnostics.UnreachableException();
            }
            var localDeclarationSymbol = _resolveName(node, name!.Text(), SymbolFlags.Variable, null /*nameNotFoundMessage*/, false /*isUse*/, false);
            if (localDeclarationSymbol != null && localDeclarationSymbol != symbol && (localDeclarationSymbol.Flags & SymbolFlags.BlockScopedVariable) != 0)
            {
                if ((GetDeclarationNodeFlagsFromSymbol(localDeclarationSymbol) & NodeFlags.BlockScoped) != 0)
                {
                    // Go: localDeclarationSymbol.ValueDeclaration 必有 VariableDeclarationList 祖先（变量声明的不变量）。
                    var varDeclList = FindAncestorKind(localDeclarationSymbol.ValueDeclaration, Kind.VariableDeclarationList)!;
                    Node? container = null;
                    if (IsVariableStatement(varDeclList.Parent!) && varDeclList.Parent!.Parent != null)
                    {
                        container = varDeclList.Parent.Parent;
                    }
                    // names of block-scoped and function scoped variables can collide only
                    // if block scoped variable is defined in the function\module\source file scope (because of variable hoisting)
                    var namesShareScope = container != null && (IsBlock(container) && IsFunctionLike(container.Parent) ||
                        IsModuleBlock(container) || IsModuleDeclaration(container) || IsSourceFile(container));
                    // here we know that function scoped variable is "shadowed" by block scoped one
                    // a var declaration can't hoist past a lexical declaration and it results in a SyntaxError at runtime
                    if (!namesShareScope)
                    {
                        // Go: 内层 `name := c.symbolToString(localDeclarationSymbol)` 遮蔽了外层 name → C# 改名 symbolName。
                        var symbolName = SymbolToStringWorker(localDeclarationSymbol);
                        Error(node, Diagnostics.Cannot_initialize_outer_scoped_variable_0_in_the_same_scope_as_block_scoped_declaration_1, symbolName, symbolName);
                    }
                }
            }
        }
    }

    internal void CheckDecorators(Node node)
    {
        // skip this check for nodes that cannot have decorators. These should have already had an error reported by
        // checkGrammarModifiers.
        if (!CanHaveDecorators(node) || !HasDecorators(node) || !NodeCanBeDecorated(_legacyDecorators, node, node.Parent, node.Parent!.Parent))
        {
            return;
        }
        var firstDecorator = Find(node.ModifierNodes(), d => IsDecorator(d));
        if (firstDecorator == null)
        {
            return;
        }
        if (_legacyDecorators)
        {
            CheckExternalEmitHelpers(firstDecorator, ExternalEmitHelpers.Decorate);
            if (IsParameterDeclaration(node))
            {
                CheckExternalEmitHelpers(firstDecorator, ExternalEmitHelpers.Param);
            }
        }
        else if (_languageVersion < LanguageFeatureMinimumTarget.ClassAndClassElementDecorators)
        {
            CheckExternalEmitHelpers(firstDecorator, ExternalEmitHelpers.ESDecorateAndRunInitializers);
            if (IsClassDeclaration(node))
            {
                if (node.Name() == null || GetFirstTransformableStaticClassElement(node) != null)
                {
                    CheckExternalEmitHelpers(firstDecorator, ExternalEmitHelpers.SetFunctionName);
                }
            }
            else if (!IsClassExpression(node))
            {
                var name = node.Name();
                if (IsPrivateIdentifier(name!) && (IsMethodDeclaration(node) || IsAccessor(node) || IsAutoAccessorPropertyDeclaration(node)))
                {
                    CheckExternalEmitHelpers(firstDecorator, ExternalEmitHelpers.SetFunctionName);
                }
                if (IsComputedPropertyName(name!))
                {
                    CheckExternalEmitHelpers(firstDecorator, ExternalEmitHelpers.PropKey);
                }
            }
        }
        MarkLinkedReferences(node, ReferenceHint.Decorator, null, null);
        foreach (var modifier in node.ModifierNodes()!)
        {
            if (IsDecorator(modifier))
            {
                CheckDecorator(modifier);
            }
        }
    }

    internal void CheckDecorator(Node node)
    {
        CheckGrammarDecorator(node.AsDecorator()!);
        Signature[]? candidatesOutArray = null;
        var signature = GetResolvedSignatureWorker(node, ref candidatesOutArray, CheckMode.Normal);
        CheckDeprecatedSignature(signature, node);
        var returnType = GetReturnTypeOfSignatureWorker(signature);
        if ((returnType._flags & TypeFlags.Any) != 0)
        {
            return;
        }
        // if we fail to get a signature and return type here, we will have already reported a grammar error in `checkDecorators`.
        var decoratorSignature = GetDecoratorCallSignature(node);
        if (decoratorSignature == null || decoratorSignature._resolvedReturnType == null)
        {
            return;
        }
        Message headMessage;
        var expectedReturnType = decoratorSignature._resolvedReturnType;
        switch (node.Parent!.Kind)
        {
            case Kind.ClassDeclaration:
            case Kind.ClassExpression:
                headMessage = Diagnostics.Decorator_function_return_type_0_is_not_assignable_to_type_1;
                break;
            case Kind.PropertyDeclaration:
                if (!_legacyDecorators)
                {
                    headMessage = Diagnostics.Decorator_function_return_type_0_is_not_assignable_to_type_1;
                    break;
                }
                // Go: fallthrough → KindParameter 分支
                headMessage = Diagnostics.Decorator_function_return_type_is_0_but_is_expected_to_be_void_or_any;
                break;
            case Kind.Parameter:
                headMessage = Diagnostics.Decorator_function_return_type_is_0_but_is_expected_to_be_void_or_any;
                break;
            case Kind.MethodDeclaration:
            case Kind.GetAccessor:
            case Kind.SetAccessor:
                headMessage = Diagnostics.Decorator_function_return_type_0_is_not_assignable_to_type_1;
                break;
            default:
                Debug.Fail("Unhandled case in checkDecorator");
                throw new System.Diagnostics.UnreachableException();
        }
        CheckTypeAssignableTo(returnType, expectedReturnType, node.Expression(), headMessage);
    }
}
