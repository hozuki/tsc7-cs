// ported from: internal/transformers/moduletransforms/esmodule.go
//
// Go → C# 形状说明（docs/architecture.md §2、docs/ast.md §2）：
// 1) Go 的嵌入 `transformers.Transformer` → C# 基类 `Transformer`（T1 提供；`EmitContext()`/`Visitor()`/`Factory()` 是方法）。
// 2) Go 的 `tx.visit` 方法 → C# 私有 `Visit(Node?)`，直接作为方法组传给 `NewTransformer(tx.Visit, opt.Context)`。
// 3) Go 的 `statements []*ast.Statement` + append → C# `List<Node>`（`Count` 等价 `len`），边界处 `.ToArray()`。
// 4) Go 的 `core.FirstResult(tx.Visitor().VisitSlice(x))` → C# `Visitor().VisitSlice(x).Result`（`FirstResult` 不移植，
//    见 Core/Core.cs 文件头；C# 的多返回值是元组）。
// 5) Go 的 `slices.Clone(slice)` → `new List<Node>(slice)`（只读视图语义等价）。
// 6) Go 的未使用字段（`helperNameSubstitutions`）声明为 `internal`：声明对齐 Go，且避免 CS0414
//    （private 字段"赋值后从未读取"在 TreatWarningsAsErrors 下会变错误）。
// 7) Go 的包级导出处方函数 `NewESModuleTransformer` → 与包同名的静态类 `ModuleTransforms` 的 static 成员（§2）。

using System;
using TypeScript.Ast;
using TypeScript.Binder;
using TypeScript.Core;
using TypeScript.Printer;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Transformers.Transformers;

namespace TypeScript.Transformers.ModuleTransforms;

/// <summary>Go: <c>type importRequireStatements struct</c>（esmodule.go:15-18）。</summary>
internal sealed class ImportRequireStatements
{
    /// <summary>Go: <c>statements</c>。</summary>
    internal List<Node> _statements = [];

    /// <summary>Go: <c>requireHelperName</c>。</summary>
    internal Node _requireHelperName = null!;
}

/// <summary>Go: <c>type ESModuleTransformer struct</c>（esmodule.go:13-21）。</summary>
public sealed class ESModuleTransformer : Transformer
{
    internal CompilerOptions _compilerOptions = null!;

    /// <summary>Go: esmodule.go:14 的 `resolver`：Go 里只被构造器赋值、后续从未读取（已 grep 确认）→ internal 对齐且避免 CS0414。</summary>
    internal ReferenceResolver? _resolver;

    internal Func<HasFileName, ModuleKind>? _getEmitModuleFormatOfFile;
    private SourceFile? _currentSourceFile;
    private ImportRequireStatements? _importRequireStatements;

    /// <summary>Go: esmodule.go:20 声明了 `helperNameSubstitutions`，但 Go 全仓从未读写它（已 grep 确认）→ 保留字段对齐。</summary>
    internal Dictionary<string, Node> _helperNameSubstitutions = new();

    /// <summary>Go: <c>func (tx *ESModuleTransformer) visit(node *ast.Node) *ast.Node</c>。</summary>
    internal Node? Visit(Node node)
    {
        // Go 把分支结果赋回 `node`（可能是 nil = 删除该节点）；C# 直接 return，避免把 Node? 赋给 Node。
        switch (node.Kind)
        {
            case Kind.SourceFile:
                return VisitSourceFile(node.AsSourceFile()!);
            case Kind.ImportDeclaration:
                return VisitImportDeclaration(node.AsImportDeclaration()!);
            case Kind.ImportEqualsDeclaration:
                return VisitImportEqualsDeclaration(node.AsImportEqualsDeclaration()!);
            case Kind.ExportAssignment:
                return VisitExportAssignment(node.AsExportAssignment()!);
            case Kind.ExportDeclaration:
                return VisitExportDeclaration(node.AsExportDeclaration()!);
            case Kind.CallExpression:
                return VisitCallExpression(node.AsCallExpression()!);
            default:
                return Visitor().VisitEachChild(node);
        }
    }

    /// <summary>Go: <c>func (tx *ESModuleTransformer) visitSourceFile(node *ast.SourceFile) *ast.Node</c>。</summary>
    private Node VisitSourceFile(SourceFile node)
    {
        if (node.IsDeclarationFile ||
            !(IsExternalModule(node) || _compilerOptions.GetIsolatedModules()))
        {
            return node;
        }

        _currentSourceFile = node;
        _importRequireStatements = null;

        var result = Visitor().VisitEachChild(node.AsNode())!.AsSourceFile()!;
        EmitContext().AddEmitHelper(result.AsNode(), EmitContext().ReadEmitHelpers());

        var externalHelpersImportDeclaration = ModuleTransforms.CreateExternalHelpersImportDeclarationIfNeeded(EmitContext(), result, _compilerOptions, _getEmitModuleFormatOfFile!(node), false /*hasExportStarsToExportValues*/, false /*hasImportStar*/, false /*hasImportDefault*/);
        if (externalHelpersImportDeclaration is not null || _importRequireStatements is not null)
        {
            var (prologue, rest) = Factory().SplitStandardPrologue(result.Statements!.Nodes!);
            var (custom, restNodes) = Factory().SplitCustomPrologue(rest ?? []);
            var statements = new List<Node>(prologue);
            statements.AddRange(custom ?? []);
            if (externalHelpersImportDeclaration is not null)
            {
                // The helpers import must be visited so that `import x = require("tslib")`
                // (TypeScript-only syntax) is transformed to `const x = require("tslib")`
                // for CJS output files via visitImportEqualsDeclaration.
                statements.Add(Visitor().VisitNode(externalHelpersImportDeclaration)!);
            }
            if (_importRequireStatements is not null)
            {
                statements.AddRange(_importRequireStatements._statements);
            }
            statements.AddRange(restNodes);
            var statementList = Factory().NewNodeList(statements.ToArray());
            statementList.Loc = result.Statements!.Loc;
            result = Factory().UpdateSourceFile(result, statementList, node.EndOfFileToken).AsSourceFile()!;
        }

        if (IsExternalModule(result) &&
            _compilerOptions.GetEmitModuleKind() != ModuleKind.Preserve &&
            !Some(result.Statements!.Nodes!, n => IsExternalModuleIndicator(n)))
        {
            var statements = new List<Node>(result.Statements!.Nodes!);
            statements.Add(ModuleTransforms.CreateEmptyImports(Factory()));
            var statementList = Factory().NewNodeList(statements.ToArray());
            statementList.Loc = result.Statements!.Loc;
            result = Factory().UpdateSourceFile(result, statementList, node.EndOfFileToken).AsSourceFile()!;
        }

        _importRequireStatements = null;
        _currentSourceFile = null;
        return result;
    }

    /// <summary>Go: <c>func (tx *ESModuleTransformer) visitImportDeclaration(node *ast.ImportDeclaration) *ast.Node</c>。</summary>
    private Node? VisitImportDeclaration(ImportDeclaration node)
    {
        if (!_compilerOptions.RewriteRelativeImportExtensions.IsTrue())
        {
            return node;
        }
        var updatedModuleSpecifier = ModuleTransforms.RewriteModuleSpecifier(EmitContext(), node.ModuleSpecifier, _compilerOptions);
        return Factory().UpdateImportDeclaration(
            node,
            null, /*modifiers*/
            Visitor().VisitNode(node.ImportClause),
            updatedModuleSpecifier,
            Visitor().VisitNode(node.Attributes));
    }

    /// <summary>Go: <c>func (tx *ESModuleTransformer) visitImportEqualsDeclaration(node *ast.ImportEqualsDeclaration) *ast.Node</c>。</summary>
    private Node? VisitImportEqualsDeclaration(ImportEqualsDeclaration node)
    {
        // Though an error in es2020 modules, in node-flavor es2020 modules, we can helpfully transform this to a synthetic `require` call
        // To give easy access to a synchronous `require` in node-flavor esm. We do the transform even in scenarios where we error, but `import.meta.url`
        // is available, just because the output is reasonable for a node-like runtime.
        if (_compilerOptions.GetEmitModuleKind() < ModuleKind.Node16)
        {
            return null;
        }

        if (!IsExternalModuleImportEqualsDeclaration(node.AsNode()))
        {
            Debug.Fail("import= for internal module references should be handled in an earlier transformer.");
        }

        var varStatement = Factory().NewVariableStatement(
            null, /*modifiers*/
            Factory().NewVariableDeclarationList(
                Factory().NewNodeList([
                    Factory().NewVariableDeclaration(
                        node.Name()!.Clone(Factory()),
                        null, /*exclamationToken*/
                        null, /*type*/
                        CreateRequireCall(node.AsNode())),
                ]),
                NodeFlags.Const));
        EmitContext().SetOriginal(varStatement, node.AsNode());
        EmitContext().AssignCommentAndSourceMapRanges(varStatement, node.AsNode());

        var statements = new List<Node> { varStatement };
        statements = AppendExportsOfImportEqualsDeclaration(statements, node);
        return SingleOrMany(statements.ToArray(), Factory());
    }

    /// <summary>Go: <c>func (tx *ESModuleTransformer) appendExportsOfImportEqualsDeclaration(statements []*ast.Statement, node *ast.ImportEqualsDeclaration) []*ast.Statement</c>。</summary>
    private List<Node> AppendExportsOfImportEqualsDeclaration(List<Node> statements, ImportEqualsDeclaration node)
    {
        if (HasSyntacticModifier(node.AsNode(), ModifierFlags.Export))
        {
            statements.Add(Factory().NewExportDeclaration(
                null,  /*modifiers*/
                false, /*isTypeOnly*/
                Factory().NewNamedExports(
                    Factory().NewNodeList([
                        Factory().NewExportSpecifier(
                            false, /*isTypeOnly*/
                            null,  /*propertyName*/
                            node.Name()!.Clone(Factory())),
                    ])),
                null, /*moduleSpecifier*/
                null  /*attributes*/
            ));
        }
        return statements;
    }

    /// <summary>Go: <c>func (tx *ESModuleTransformer) visitExportAssignment(node *ast.ExportAssignment) *ast.Node</c>。</summary>
    private Node? VisitExportAssignment(ExportAssignment node)
    {
        if (!node.IsExportEquals)
        {
            return Visitor().VisitEachChild(node.AsNode());
        }
        if (_compilerOptions.GetEmitModuleKind() != ModuleKind.Preserve)
        {
            // Elide `export=` as it is not legal with --module ES6
            return null;
        }
        var statement = Factory().NewExpressionStatement(
            Factory().NewAssignmentExpression(
                Factory().NewPropertyAccessExpression(
                    Factory().NewIdentifier("module"),
                    null, /*questionDotToken*/
                    Factory().NewIdentifier("exports"),
                    NodeFlags.None),
                Visitor().VisitNode(node.Expression)!));
        EmitContext().SetOriginal(statement, node.AsNode());
        return statement;
    }

    /// <summary>Go: <c>func (tx *ESModuleTransformer) visitExportDeclaration(node *ast.ExportDeclaration) *ast.Node</c>。</summary>
    private Node? VisitExportDeclaration(ExportDeclaration node)
    {
        if (node.ModuleSpecifier is null)
        {
            return node;
        }

        var updatedModuleSpecifier = ModuleTransforms.RewriteModuleSpecifier(EmitContext(), node.ModuleSpecifier, _compilerOptions);
        if (_compilerOptions.Module > ModuleKind.ES2015 || node.ExportClause is null || !IsNamespaceExport(node.ExportClause))
        {
            // Either ill-formed or don't need to be transformed.
            return Factory().UpdateExportDeclaration(
                node,
                null,  /*modifiers*/
                false, /*isTypeOnly*/
                node.ExportClause,
                updatedModuleSpecifier,
                Visitor().VisitNode(node.Attributes));
        }

        var oldIdentifier = node.ExportClause.Name()!;
        var synthName = Factory().NewGeneratedNameForNode(oldIdentifier);
        var importDecl = Factory().NewImportDeclaration(
            null, /*modifiers*/
            Factory().NewImportClause(
                Kind.Unknown, /*phaseModifier*/
                null,         /*name*/
                Factory().NewNamespaceImport(synthName)),
            updatedModuleSpecifier,
            Visitor().VisitNode(node.Attributes));
        EmitContext().SetOriginal(importDecl, node.ExportClause);

        Node exportDecl;
        if (IsExportNamespaceAsDefaultDeclaration(node.AsNode()))
        {
            exportDecl = Factory().NewExportAssignment(null /*modifiers*/, false /*isExportEquals*/, null /*typeNode*/, synthName);
        }
        else
        {
            exportDecl = Factory().NewExportDeclaration(
                null,  /*modifiers*/
                false, /*isTypeOnly*/
                Factory().NewNamedExports(
                    Factory().NewNodeList([
                        Factory().NewExportSpecifier(false /*isTypeOnly*/, synthName, oldIdentifier),
                    ])),
                null, /*moduleSpecifier*/
                null  /*attributes*/
            );
        }
        EmitContext().SetOriginal(exportDecl, node.AsNode());
        return SingleOrMany([importDecl, exportDecl], Factory());
    }

    /// <summary>Go: <c>func (tx *ESModuleTransformer) visitCallExpression(node *ast.CallExpression) *ast.Node</c>。</summary>
    private Node? VisitCallExpression(CallExpression node)
    {
        if (_compilerOptions.RewriteRelativeImportExtensions.IsTrue())
        {
            if (IsImportCall(node.AsNode()) && node.Arguments!.Nodes!.Length > 0 ||
                IsInJSFile(node.AsNode()) && IsRequireCall(node.AsNode(), false /*requireStringLiteralLikeArgument*/))
            {
                return VisitImportOrRequireCall(node);
            }
        }
        return Visitor().VisitEachChild(node.AsNode());
    }

    /// <summary>Go: <c>func (tx *ESModuleTransformer) visitImportOrRequireCall(node *ast.CallExpression) *ast.Node</c>。</summary>
    private Node? VisitImportOrRequireCall(CallExpression node)
    {
        if (node.Arguments!.Nodes!.Length == 0)
        {
            return Visitor().VisitEachChild(node.AsNode());
        }

        var expression = Visitor().VisitNode(node.Expression)!;

        Node? argument;
        if (IsStringLiteralLike(node.Arguments.Nodes[0]))
        {
            argument = ModuleTransforms.RewriteModuleSpecifier(EmitContext(), node.Arguments.Nodes[0], _compilerOptions);
        }
        else
        {
            argument = Factory().NewRewriteRelativeImportExtensionsHelper(node.Arguments.Nodes[0], _compilerOptions.Jsx == JsxEmit.Preserve);
        }

        var arguments = new List<Node> { argument! };
        var (rest, _) = Visitor().VisitSlice(node.Arguments.Nodes[1..]);
        arguments.AddRange(rest ?? []);

        var argumentList = Factory().NewNodeList(arguments.ToArray());
        argumentList.Loc = node.Arguments.Loc;
        return Factory().UpdateCallExpression(
            node,
            expression,
            node.QuestionDotToken,
            null, /*typeArguments*/
            argumentList,
            node.Flags);
    }

    /// <summary>Go: <c>func (tx *ESModuleTransformer) createRequireCall(node *ast.Node) *ast.Expression</c>。</summary>
    private Node CreateRequireCall(Node node /*ImportDeclaration | ImportEqualsDeclaration | ExportDeclaration*/)
    {
        var moduleName = ModuleTransforms.GetExternalModuleNameLiteral(Factory(), node, _currentSourceFile!, null /*host*/, null /*emitResolver*/, _compilerOptions);

        var args = new List<Node>();
        if (moduleName is not null)
        {
            args.Add(ModuleTransforms.RewriteModuleSpecifier(EmitContext(), moduleName, _compilerOptions)!);
        }

        if (_compilerOptions.GetEmitModuleKind() == ModuleKind.Preserve)
        {
            return Factory().NewCallExpression(
                Factory().NewIdentifier("require"),
                null, /*questionDotToken*/
                null, /*typeArguments*/
                Factory().NewNodeList(args.ToArray()),
                NodeFlags.None);
        }

        if (_importRequireStatements is null)
        {
            var createRequireName = Factory().NewUniqueNameEx("_createRequire", new AutoGenerateOptions { Flags = GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.FileLevel });
            var importStatement = Factory().NewImportDeclaration(
                null, /*modifiers*/
                Factory().NewImportClause(
                    Kind.Unknown, /*phaseModifier*/
                    null,         /*name*/
                    Factory().NewNamedImports(
                        Factory().NewNodeList([
                            Factory().NewImportSpecifier(
                                false, /*isTypeOnly*/
                                Factory().NewIdentifier("createRequire"),
                                createRequireName),
                        ]))),
                Factory().NewStringLiteral("module", TokenFlags.None),
                null /*attributes*/);
            EmitContext().AddEmitFlags(importStatement, EmitFlags.CustomPrologue);

            var requireHelperName = Factory().NewUniqueNameEx("__require", new AutoGenerateOptions { Flags = GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.FileLevel });
            var requireStatement = Factory().NewVariableStatement(
                null, /*modifiers*/
                Factory().NewVariableDeclarationList(
                    Factory().NewNodeList([
                        Factory().NewVariableDeclaration(
                            requireHelperName,
                            null, /*exclamationToken*/
                            null, /*type*/
                            Factory().NewCallExpression(
                                createRequireName.Clone(Factory()),
                                null, /*questionDotToken*/
                                null, /*typeArguments*/
                                Factory().NewNodeList([
                                    Factory().NewPropertyAccessExpression(
                                        Factory().NewMetaProperty(Kind.ImportKeyword, Factory().NewIdentifier("meta")),
                                        null, /*questionDotToken*/
                                        Factory().NewIdentifier("url"),
                                        NodeFlags.None),
                                ]),
                                NodeFlags.None)),
                    ]),
                    NodeFlags.Const));
            EmitContext().AddEmitFlags(requireStatement, EmitFlags.CustomPrologue);
            _importRequireStatements = new ImportRequireStatements
            {
                _statements = [importStatement, requireStatement],
                _requireHelperName = requireHelperName,
            };
        }

        return Factory().NewCallExpression(
            _importRequireStatements._requireHelperName.Clone(Factory()),
            null, /*questionDotToken*/
            null, /*typeArguments*/
            Factory().NewNodeList(args.ToArray()),
            NodeFlags.None);
    }
}

/// <summary>Go: moduletransforms 包的包级导出处方函数。</summary>
public static partial class ModuleTransforms
{
    /// <summary>Go: <c>func NewESModuleTransformer(opts *transformers.TransformOptions) *transformers.Transformer</c>。</summary>
    public static Transformer? NewESModuleTransformer(TransformOptions opts)
    {
        var compilerOptions = opts.CompilerOptions;
        var tx = new ESModuleTransformer
        {
            _compilerOptions = compilerOptions!,
            _resolver = opts.Resolver,
            _getEmitModuleFormatOfFile = opts.GetEmitModuleFormatOfFile,
        };
        return tx.NewTransformer(tx.Visit, opts.Context);
    }
}
