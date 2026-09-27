// ported from: internal/transformers/tstransforms/typeeraser.go:1-393
//
// 与 Go 的差异（语言特性；其余见 docs/architecture.md §2/§3）：
// 1) Go 的嵌入 `transformers.Transformer` → 基类继承；`tx.visit` 方法组传给 `NewTransformer`。
// 2) Go 的 `defer tx.popNode(grandparentNode)` → `try/finally`（C# 没有 defer；等效）。
// 3) `NewTypeEraserTransformer` 是 Go 的包级函数 → 同类上的 `public static` 工厂（先例 Printer.NewPrinter）。
// 4) Go 的 `(grandparentNode *ast.Node)` 具名返回值 → C# 普通返回值 `Node?`。
// 5) `slices.Concat(modifiers.Nodes, visited)` → `Core.Concatenate(modifiers.Nodes, visited)`
//    （docs §3.8：不引入 LINQ/多余拷贝）。
// 6) Go 的 `ast.OEKAllExceptAssertionsOrExpressionsWithTypeArguments` →
//    `OuterExpressionKinds.AllExceptAssertionsOrExpressionsWithTypeArguments`（常量名去掉类型前缀，
//    docs/architecture.md §3.3 同源规则）。
// 7) `getInnermostModuleDeclarationFromDottedModule`（Go 定义在 runtimesyntax.go）是包级函数 →
//    落在静态类 `TsTransforms`（见 RuntimeSyntax.cs）。
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Printer;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Transformers.Transformers;
using static TypeScript.Transformers.TsTransforms.TsTransforms;

namespace TypeScript.Transformers.TsTransforms;

/// <summary>Go: <c>type TypeEraserTransformer struct</c>。</summary>
public sealed class TypeEraserTransformer : Transformer
{
    private readonly CompilerOptions _compilerOptions;
    private Node? _parentNode;
    private Node? _currentNode;

    private TypeEraserTransformer(CompilerOptions compilerOptions)
    {
        _compilerOptions = compilerOptions;
    }

    /// <summary>Go: <c>func NewTypeEraserTransformer(opt *transformers.TransformOptions) *transformers.Transformer</c>。</summary>
    public static Transformer? NewTypeEraserTransformer(TransformOptions opt)
    {
        var compilerOptions = opt.CompilerOptions!;
        var emitContext = opt.Context;
        var tx = new TypeEraserTransformer(compilerOptions);
        return tx.NewTransformer(tx.Visit, emitContext);
    }

    /// <summary>
    /// Pushes a new child node onto the ancestor tracking stack, returning the grandparent node to be
    /// restored later via <see cref="PopNode"/>.
    /// </summary>
    private Node? PushNode(Node node)
    {
        var grandparentNode = _parentNode;
        _parentNode = _currentNode;
        _currentNode = node;
        return grandparentNode;
    }

    /// <summary>Pops the last child node off the ancestor tracking stack, restoring the grandparent node.</summary>
    private void PopNode(Node? grandparentNode)
    {
        _currentNode = _parentNode;
        _parentNode = grandparentNode;
    }

    private Node Elide(Node node)
    {
        return EmitContext().NewNotEmittedStatement(node);
    }

    private Node? Visit(Node node)
    {
        if ((node.SubtreeFacts & SubtreeFacts.ContainsTypeScript) == 0)
        {
            return node;
        }

        if (IsStatement(node) && HasSyntacticModifier(node, ModifierFlags.Ambient))
        {
            return Elide(node);
        }

        var grandparentNode = PushNode(node);
        try
        {
            switch (node.Kind)
            {
                case
                    // TypeScript accessibility and readonly modifiers are elided
                    Kind.PublicKeyword:
                case Kind.PrivateKeyword:
                case Kind.ProtectedKeyword:
                case Kind.AbstractKeyword:
                case Kind.OverrideKeyword:
                case Kind.ConstKeyword:
                case Kind.DeclareKeyword:
                case Kind.ReadonlyKeyword:
                // TypeScript type nodes are elided.
                case Kind.ArrayType:
                case Kind.TupleType:
                case Kind.OptionalType:
                case Kind.RestType:
                case Kind.TypeLiteral:
                case Kind.TypePredicate:
                case Kind.TypeParameter:
                case Kind.AnyKeyword:
                case Kind.UnknownKeyword:
                case Kind.BooleanKeyword:
                case Kind.StringKeyword:
                case Kind.NumberKeyword:
                case Kind.NeverKeyword:
                case Kind.VoidKeyword:
                case Kind.SymbolKeyword:
                case Kind.ConstructorType:
                case Kind.FunctionType:
                case Kind.TypeQuery:
                case Kind.TypeReference:
                case Kind.UnionType:
                case Kind.IntersectionType:
                case Kind.ConditionalType:
                case Kind.ParenthesizedType:
                case Kind.ThisType:
                case Kind.TypeOperator:
                case Kind.IndexedAccessType:
                case Kind.MappedType:
                case Kind.LiteralType:
                // TypeScript index signatures are elided.
                case Kind.IndexSignature:
                    return null;

                case Kind.InKeyword:
                case Kind.OutKeyword:
                    // TypeScript `in`/`out` variance modifiers are elided. These keywords are only
                    // meaningful as modifiers on type parameters (which are themselves elided), but they may
                    // appear as a grammar error on other declarations and must not leak into the emitted JS.
                    // The `in` binary operator shares this token kind, so only elide when used as a modifier.
                    if (_parentNode is null || !IsBinaryExpression(_parentNode))
                    {
                        return null;
                    }

                    return Visitor().VisitEachChild(node);

                case Kind.JSImportDeclaration:
                    // reparsed commonjs are elided
                    return null;

                case Kind.TypeAliasDeclaration:
                case Kind.JSTypeAliasDeclaration:
                case Kind.InterfaceDeclaration:
                    // TypeScript type-only declarations are elided.
                    return Elide(node);

                case Kind.NamespaceExportDeclaration:
                    // TypeScript namespace export declarations are elided.
                    return null;

                case Kind.ModuleDeclaration:
                    if (!IsIdentifier(node.Name()!) ||
                        !IsInstantiatedModule(node, _compilerOptions.ShouldPreserveConstEnums()) ||
                        GetInnermostModuleDeclarationFromDottedModule(node.AsModuleDeclaration()!).Body is null)
                    {
                        // TypeScript module declarations are elided if they are not instantiated or have no body
                        return Elide(node);
                    }

                    return Visitor().VisitEachChild(node);

                case Kind.ExpressionWithTypeArguments:
                {
                    var n = node.AsExpressionWithTypeArguments()!;
                    return Factory().UpdateExpressionWithTypeArguments(n, Visitor().VisitNode(n.Expression), null);
                }

                case Kind.PropertyDeclaration:
                {
                    if (_compilerOptions.ExperimentalDecorators.IsTrue() && HasSyntacticModifier(node, ModifierFlags.Ambient | ModifierFlags.Abstract) && HasDecorators(node))
                    {
                        // declare/abstract props with decorators must be preserved until the decorator transform can process them and remove them
                        var n = node.AsPropertyDeclaration()!;
                        return Factory().UpdatePropertyDeclaration(n, Visitor().VisitModifiers(n.Modifiers()), Visitor().VisitNode(n.Name()), null, null, Visitor().VisitNode(n.Initializer));
                    }

                    if (HasSyntacticModifier(node, ModifierFlags.Ambient | ModifierFlags.Abstract))
                    {
                        // TypeScript `declare` fields are elided
                        return null;
                    }

                    var p = node.AsPropertyDeclaration()!;
                    return Factory().UpdatePropertyDeclaration(p, Visitor().VisitModifiers(p.Modifiers()), Visitor().VisitNode(p.Name()), null, null, Visitor().VisitNode(p.Initializer));
                }

                case Kind.Constructor:
                {
                    var n = node.AsConstructorDeclaration()!;
                    if (NodeIsMissing(n.Body))
                    {
                        // TypeScript overloads are elided
                        return null;
                    }

                    return Factory().UpdateConstructorDeclaration(n, null, null, Visitor().VisitNodes(n.Parameters), null, null, Visitor().VisitNode(n.Body));
                }

                case Kind.MethodDeclaration:
                {
                    var n = node.AsMethodDeclaration()!;
                    if (NodeIsMissing(n.Body))
                    {
                        // TypeScript overloads are elided
                        return null;
                    }

                    return Factory().UpdateMethodDeclaration(n, Visitor().VisitModifiers(n.Modifiers()), n.AsteriskToken, Visitor().VisitNode(n.Name()), null, null, Visitor().VisitNodes(n.Parameters), null, null, Visitor().VisitNode(n.Body));
                }

                case Kind.GetAccessor:
                {
                    var n = node.AsGetAccessorDeclaration()!;
                    if (NodeIsMissing(n.Body) && HasSyntacticModifier(node, ModifierFlags.Abstract))
                    {
                        // Abstract accessors are elided
                        return null;
                    }

                    var body = Visitor().VisitNode(n.Body);
                    if (body is null)
                    {
                        body = Factory().NewBlock(Factory().NewNodeList(null), false);
                    }

                    return Factory().UpdateGetAccessorDeclaration(n, Visitor().VisitModifiers(n.Modifiers()), Visitor().VisitNode(n.Name()), null, Visitor().VisitNodes(n.Parameters), null, null, body);
                }

                case Kind.SetAccessor:
                {
                    var n = node.AsSetAccessorDeclaration()!;
                    if (NodeIsMissing(n.Body) && HasSyntacticModifier(node, ModifierFlags.Abstract))
                    {
                        // Abstract accessors are elided
                        return null;
                    }

                    var body = Visitor().VisitNode(n.Body);
                    if (body is null)
                    {
                        body = Factory().NewBlock(Factory().NewNodeList(null), false);
                    }

                    return Factory().UpdateSetAccessorDeclaration(n, Visitor().VisitModifiers(n.Modifiers()), Visitor().VisitNode(n.Name()), null, Visitor().VisitNodes(n.Parameters), null, null, body);
                }

                case Kind.VariableDeclaration:
                {
                    var n = node.AsVariableDeclaration()!;
                    var updated = Factory().UpdateVariableDeclaration(n, Visitor().VisitNode(n.Name()), null, null, Visitor().VisitNode(n.Initializer));
                    if (n.Type is not null)
                    {
                        EmitContext().SetTypeNode(updated.AsVariableDeclaration()!.Name()!, n.Type);
                    }

                    return updated;
                }

                case Kind.HeritageClause:
                {
                    var n = node.AsHeritageClause()!;
                    if (n.Token == Kind.ImplementsKeyword)
                    {
                        // TypeScript `implements` clauses are elided
                        return null;
                    }

                    return Factory().UpdateHeritageClause(n, n.Token, Visitor().VisitNodes(n.Types));
                }

                case Kind.ClassDeclaration:
                {
                    var n = node.AsClassDeclaration()!;
                    return Factory().UpdateClassDeclaration(n, Visitor().VisitModifiers(n.Modifiers()), Visitor().VisitNode(n.Name()), null, Visitor().VisitNodes(n.HeritageClauses), Visitor().VisitNodes(n.Members));
                }

                case Kind.ClassExpression:
                {
                    var n = node.AsClassExpression()!;
                    return Factory().UpdateClassExpression(n, Visitor().VisitModifiers(n.Modifiers()), Visitor().VisitNode(n.Name()), null, Visitor().VisitNodes(n.HeritageClauses), Visitor().VisitNodes(n.Members));
                }

                case Kind.FunctionDeclaration:
                {
                    var n = node.AsFunctionDeclaration()!;
                    if (NodeIsMissing(n.Body))
                    {
                        // TypeScript overloads are elided
                        return Elide(node);
                    }

                    return Factory().UpdateFunctionDeclaration(n, Visitor().VisitModifiers(n.Modifiers()), n.AsteriskToken, Visitor().VisitNode(n.Name()), null, Visitor().VisitNodes(n.Parameters), null, null, Visitor().VisitNode(n.Body));
                }

                case Kind.FunctionExpression:
                {
                    var n = node.AsFunctionExpression()!;
                    return Factory().UpdateFunctionExpression(n, Visitor().VisitModifiers(n.Modifiers()), n.AsteriskToken, Visitor().VisitNode(n.Name()), null, Visitor().VisitNodes(n.Parameters), null, null, Visitor().VisitNode(n.Body));
                }

                case Kind.ArrowFunction:
                {
                    var n = node.AsArrowFunction()!;
                    return Factory().UpdateArrowFunction(n, Visitor().VisitModifiers(n.Modifiers()), null, Visitor().VisitNodes(n.Parameters), null, null, n.EqualsGreaterThanToken, Visitor().VisitNode(n.Body));
                }

                case Kind.Parameter:
                {
                    if (IsThisParameter(node))
                    {
                        // TypeScript `this` parameters are elided
                        return null;
                    }

                    var n = node.AsParameterDeclaration()!;
                    // preserve parameter property modifiers to be handled by the runtime transformer
                    ModifierList? modifiers = null;
                    if (IsParameterPropertyDeclaration(node, _parentNode))
                    {
                        modifiers = ExtractModifiers(EmitContext(), n.Modifiers(), ModifierFlags.ParameterPropertyModifier);
                    }

                    // preserve decorators for the decorator transforms
                    if (HasDecorators(node))
                    {
                        var decorators = node.Decorators();
                        var (visited, _) = Visitor().VisitSlice(decorators);
                        if (modifiers is null)
                        {
                            modifiers = Factory().NewModifierList(visited);
                        }
                        else
                        {
                            modifiers = Factory().NewModifierList(Concatenate(modifiers.Nodes, visited));
                        }
                    }

                    return Factory().UpdateParameterDeclaration(n, modifiers, n.DotDotDotToken, Visitor().VisitNode(n.Name()), null, null, Visitor().VisitNode(n.Initializer));
                }

                case Kind.CallExpression:
                {
                    var n = node.AsCallExpression()!;
                    return Factory().UpdateCallExpression(n, Visitor().VisitNode(n.Expression), n.QuestionDotToken, null, Visitor().VisitNodes(n.Arguments), n.Flags);
                }

                case Kind.NewExpression:
                {
                    var n = node.AsNewExpression()!;
                    return Factory().UpdateNewExpression(n, Visitor().VisitNode(n.Expression), null, Visitor().VisitNodes(n.Arguments));
                }

                case Kind.TaggedTemplateExpression:
                {
                    var n = node.AsTaggedTemplateExpression()!;
                    return Factory().UpdateTaggedTemplateExpression(n, Visitor().VisitNode(n.Tag), n.QuestionDotToken, null, Visitor().VisitNode(n.Template), n.Flags);
                }

                case Kind.NonNullExpression:
                case Kind.TypeAssertionExpression:
                case Kind.AsExpression:
                case Kind.SatisfiesExpression:
                {
                    var partial = Factory().NewPartiallyEmittedExpression(Visitor().VisitNode(node.Expression()!));
                    EmitContext().SetOriginal(partial, node);
                    partial.Loc = node.Loc;
                    return partial;
                }

                case Kind.ParenthesizedExpression:
                {
                    if (!IsJSDocTypeAssertion(node))
                    {
                        var n = node.AsParenthesizedExpression()!;
                        var expression = SkipOuterExpressions(n.Expression, OuterExpressionKinds.AllExceptAssertionsOrExpressionsWithTypeArguments);
                        if (IsAssertionExpression(expression) || IsSatisfiesExpression(expression!))
                        {
                            var partial = Factory().NewPartiallyEmittedExpression(Visitor().VisitNode(n.Expression));
                            EmitContext().SetOriginal(partial, node);
                            partial.Loc = node.Loc;
                            return partial;
                        }
                    }

                    return Visitor().VisitEachChild(node);
                }

                case Kind.JsxSelfClosingElement:
                {
                    var n = node.AsJsxSelfClosingElement()!;
                    return Factory().UpdateJsxSelfClosingElement(n, Visitor().VisitNode(n.TagName), null, Visitor().VisitNode(n.Attributes));
                }

                case Kind.JsxOpeningElement:
                {
                    var n = node.AsJsxOpeningElement()!;
                    return Factory().UpdateJsxOpeningElement(n, Visitor().VisitNode(n.TagName), null, Visitor().VisitNode(n.Attributes));
                }

                case Kind.ImportEqualsDeclaration:
                {
                    var n = node.AsImportEqualsDeclaration()!;
                    if (n.IsTypeOnly)
                    {
                        // elide type-only imports
                        return null;
                    }

                    return Visitor().VisitEachChild(node);
                }

                case Kind.ImportDeclaration:
                {
                    var n = node.AsImportDeclaration()!;
                    if (n.ImportClause is null)
                    {
                        // Do not elide a side-effect only import declaration.
                        //  import "foo";
                        return node;
                    }

                    var importClause = Visitor().VisitNode(n.ImportClause);
                    if (importClause is null)
                    {
                        return null;
                    }

                    return Factory().UpdateImportDeclaration(n, n.Modifiers(), importClause, n.ModuleSpecifier, n.Attributes);
                }

                case Kind.ImportClause:
                {
                    var n = node.AsImportClause()!;
                    if (n.IsTypeOnly())
                    {
                        // Always elide type-only imports
                        return null;
                    }

                    var name = n.Name();
                    var namedBindings = Visitor().VisitNode(n.NamedBindings);
                    if (name is null && namedBindings is null)
                    {
                        // all import bindings were elided
                        return null;
                    }

                    return Factory().UpdateImportClause(n, n.PhaseModifier, name, namedBindings);
                }

                case Kind.NamedImports:
                {
                    var n = node.AsNamedImports()!;
                    if (n.Elements!.Nodes is null || n.Elements.Nodes.Length == 0)
                    {
                        // Do not elide a side-effect only import declaration.
                        return node;
                    }

                    var elements = Visitor().VisitNodes(n.Elements);
                    if (!_compilerOptions.VerbatimModuleSyntax.IsTrue() && (elements!.Nodes is null || elements.Nodes.Length == 0))
                    {
                        // all import specifiers were elided
                        return null;
                    }

                    return Factory().UpdateNamedImports(n, elements);
                }

                case Kind.ImportSpecifier:
                {
                    var n = node.AsImportSpecifier()!;
                    if (n.IsTypeOnly)
                    {
                        // elide type-only or unused imports
                        return null;
                    }

                    return node;
                }

                case Kind.ExportDeclaration:
                {
                    var n = node.AsExportDeclaration()!;
                    if (n.IsTypeOnly)
                    {
                        // elide type-only exports
                        return null;
                    }

                    Node? exportClause = null;
                    if (n.ExportClause is not null)
                    {
                        exportClause = Visitor().VisitNode(n.ExportClause);
                        if (exportClause is null)
                        {
                            // all export bindings were elided
                            return null;
                        }
                    }

                    return Factory().UpdateExportDeclaration(n, null /*modifiers*/, false /*isTypeOnly*/, exportClause, Visitor().VisitNode(n.ModuleSpecifier), Visitor().VisitNode(n.Attributes));
                }

                case Kind.NamedExports:
                {
                    var n = node.AsNamedExports()!;
                    if (n.Elements!.Nodes is null || n.Elements.Nodes.Length == 0)
                    {
                        // Do not elide an empty export declaration.
                        return node;
                    }

                    var elements = Visitor().VisitNodes(n.Elements);
                    if (!_compilerOptions.VerbatimModuleSyntax.IsTrue() && (elements!.Nodes is null || elements.Nodes.Length == 0))
                    {
                        // all export specifiers were elided
                        return null;
                    }

                    return Factory().UpdateNamedExports(n, elements);
                }

                case Kind.ExportSpecifier:
                {
                    var n = node.AsExportSpecifier()!;
                    if (n.IsTypeOnly)
                    {
                        // elide unused export
                        return null;
                    }

                    return node;
                }

                case Kind.EnumDeclaration:
                    if (IsEnumConst(node))
                    {
                        return node;
                    }

                    return Visitor().VisitEachChild(node);

                default:
                    return Visitor().VisitEachChild(node);
            }
        }
        finally
        {
            PopNode(grandparentNode);
        }
    }
}
