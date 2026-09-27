// ported from: internal/transformers/moduletransforms/commonjsmodule.go:1-710
// （CommonJS 变换器骨架：字段/构造、祖先栈与访问器分派、SourceFile 变换、export= 追加、
//   `exports.x` 语句与表达式的生成、`require()` 调用与 helper 包装）
//
// Go → C# 形状说明（docs/architecture.md §2、docs/ast.md §2）：
// 1) Go 的嵌入 `transformers.Transformer` → C# 基类 `Transformer`；`EmitContext()`/`Visitor()`/`Factory()` 是方法。
// 2) Go 的 `defer tx.popNode(g)` → C# `try/finally`（祖先栈必须成对恢复）。
// 3) Go 的 `statements []*ast.Statement` + append → C# `List<Node>`（`Count` 等价 `len`，原地追加等价 append），
//    只在 `NewNodeList`/`SingleOrMany`/`EndAndMergeVariableEnvironment` 边界 `.ToArray()`。
// 4) Go 的 `node.SubtreeFacts()`/`node.ModifierFlags()` 在 C# 是**属性**（名字与类型同名，docs/ast.md §2）→ 去掉括号。
// 5) Go 的 `nl *core.TextRange`（可空指针）→ C# `TextRange?`（值类型可空；Go 的取址 `&x.Loc` → 传值副本，
//    被调用方只用它做 SetCommentRange，语义一致）。
// 6) Go 的 `core.FirstResult(Visitor().VisitSlice(x))` → C# `Visitor().VisitSlice(x).Result`（`FirstResult` 不移植，见 Core/Core.cs）。
// 7) Go 的包级导出处方函数 `NewCommonJSModuleTransformer` → 与包同名的静态类 `ModuleTransforms`（§2）。

using System;
using TypeScript.Ast;
using TypeScript.Binder;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.Printer;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.TSPath.TSPath;
using static TypeScript.Transformers.Transformers;

namespace TypeScript.Transformers.ModuleTransforms;

/// <summary>Go: <c>type CommonJSModuleTransformer struct</c>（commonjsmodule.go:15-31）。</summary>
public sealed partial class CommonJSModuleTransformer : Transformer
{
    // 说明：Go 未导出字段一律 `_camelCase`；被同程序集的其它类（静态类 ModuleTransforms 的处方函数、其它
    // partial 分片）读写的一律 `internal`（见 docs/architecture.md §2「编辑纪律」）。
    internal NodeVisitor _topLevelVisitor = null!;          // visits statements at top level of a module
    internal NodeVisitor _topLevelNestedVisitor = null!;    // visits nested statements at top level of a module
    internal NodeVisitor _discardedValueVisitor = null!;    // visits expressions whose values would be discarded at runtime
    internal NodeVisitor _assignmentPatternVisitor = null!; // visits assignment patterns in a destructuring assignment
    internal CompilerOptions _compilerOptions = null!;
    internal ReferenceResolver? _resolver;
    internal Func<HasFileName, ModuleKind>? _getEmitModuleFormatOfFile;
    internal ModuleKind _moduleKind;
    internal ScriptTarget _languageVersion;
    private SourceFile? _currentSourceFile;
    private ExternalModuleInfo? _currentModuleInfo;
    private Node? _parentNode;  // used for ancestor tracking via pushNode/popNode to detect expression identifiers
    private Node? _currentNode; // used for ancestor tracking via pushNode/popNode to detect expression identifiers

    // Pushes a new child node onto the ancestor tracking stack, returning the grandparent node to be restored later via `popNode`.
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) pushNode(node *ast.Node) (grandparentNode *ast.Node)</c>。</summary>
    private Node? PushNode(Node node)
    {
        var grandparentNode = _parentNode;
        _parentNode = _currentNode;
        _currentNode = node;
        return grandparentNode;
    }

    // Pops the last child node off the ancestor tracking stack, restoring the grandparent node.
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) popNode(grandparentNode *ast.Node)</c>。</summary>
    private void PopNode(Node? grandparentNode)
    {
        _currentNode = _parentNode;
        _parentNode = grandparentNode;
    }

    // Visits a node at the top level of the source file.
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitTopLevel(node *ast.Node) *ast.Node</c>。</summary>
    internal Node? VisitTopLevel(Node node)
    {
        var grandparentNode = PushNode(node);
        try
        {
            switch (node.Kind)
            {
                case Kind.ImportDeclaration:
                    node = VisitTopLevelImportDeclaration(node.AsImportDeclaration()!)!;
                    break;
                case Kind.ImportEqualsDeclaration:
                    node = VisitTopLevelImportEqualsDeclaration(node.AsImportEqualsDeclaration()!)!;
                    break;
                case Kind.ExportDeclaration:
                    node = VisitTopLevelExportDeclaration(node.AsExportDeclaration()!)!;
                    break;
                case Kind.ExportAssignment:
                    node = VisitTopLevelExportAssignment(node.AsExportAssignment()!)!;
                    break;
                case Kind.FunctionDeclaration:
                    node = VisitTopLevelFunctionDeclaration(node.AsFunctionDeclaration()!)!;
                    break;
                case Kind.ClassDeclaration:
                    node = VisitTopLevelClassDeclaration(node.AsClassDeclaration()!)!;
                    break;
                case Kind.VariableStatement:
                    node = VisitTopLevelVariableStatement(node.AsVariableStatement()!)!;
                    break;
                default:
                    node = VisitTopLevelNestedNoStack(node)!;
                    break;
            }
            return node;
        }
        finally
        {
            PopNode(grandparentNode);
        }
    }

    // Visits nested elements at the top-level of a module.
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitTopLevelNested(node *ast.Node) *ast.Node</c>。</summary>
    internal Node? VisitTopLevelNested(Node node)
    {
        var grandparentNode = PushNode(node);
        try
        {
            return VisitTopLevelNestedNoStack(node);
        }
        finally
        {
            PopNode(grandparentNode);
        }
    }

    // Visits nested elements at the top-level of a module without ancestor tracking.
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitTopLevelNestedNoStack(node *ast.Node) *ast.Node</c>。</summary>
    private Node? VisitTopLevelNestedNoStack(Node node)
    {
        switch (node.Kind)
        {
            case Kind.VariableStatement:
                node = VisitTopLevelVariableStatement(node.AsVariableStatement()!)!;
                break;
            case Kind.ForStatement:
                node = VisitTopLevelNestedForStatement(node.AsForStatement()!)!;
                break;
            case Kind.ForInStatement:
            case Kind.ForOfStatement:
                node = VisitTopLevelNestedForInOrOfStatement(node.AsForInOrOfStatement()!)!;
                break;
            case Kind.DoStatement:
                node = VisitTopLevelNestedDoStatement(node.AsDoStatement()!)!;
                break;
            case Kind.WhileStatement:
                node = VisitTopLevelNestedWhileStatement(node.AsWhileStatement()!)!;
                break;
            case Kind.LabeledStatement:
                node = VisitTopLevelNestedLabeledStatement(node.AsLabeledStatement()!)!;
                break;
            case Kind.WithStatement:
                node = VisitTopLevelNestedWithStatement(node.AsWithStatement()!)!;
                break;
            case Kind.IfStatement:
                node = VisitTopLevelNestedIfStatement(node.AsIfStatement()!)!;
                break;
            case Kind.SwitchStatement:
                node = VisitTopLevelNestedSwitchStatement(node.AsSwitchStatement()!)!;
                break;
            case Kind.CaseBlock:
                node = VisitTopLevelNestedCaseBlock(node.AsCaseBlock()!)!;
                break;
            case Kind.CaseClause:
            case Kind.DefaultClause:
                node = VisitTopLevelNestedCaseOrDefaultClause(node.AsCaseOrDefaultClause()!)!;
                break;
            case Kind.TryStatement:
                node = VisitTopLevelNestedTryStatement(node.AsTryStatement()!)!;
                break;
            case Kind.CatchClause:
                node = VisitTopLevelNestedCatchClause(node.AsCatchClause()!)!;
                break;
            case Kind.Block:
                node = VisitTopLevelNestedBlock(node.AsBlock()!)!;
                break;
            default:
                node = VisitNoStack(node, false /*resultIsDiscarded*/)!;
                break;
        }
        return node;
    }

    // Visits source elements that are not top-level or top-level nested statements.
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visit(node *ast.Node) *ast.Node</c>。</summary>
    internal Node? Visit(Node node)
    {
        var grandparentNode = PushNode(node);
        try
        {
            return VisitNoStack(node, false /*resultIsDiscarded*/);
        }
        finally
        {
            PopNode(grandparentNode);
        }
    }

    // Visits source elements that are not top-level or top-level nested statements without ancestor tracking.
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitNoStack(node *ast.Node, resultIsDiscarded bool) *ast.Node</c>。</summary>
    private Node? VisitNoStack(Node node, bool resultIsDiscarded)
    {
        // This visitor does not need to descend into the tree if there are no dynamic imports or identifiers in the subtree
        if (!IsSourceFile(node) && (node.SubtreeFacts & (SubtreeFacts.ContainsDynamicImport | SubtreeFacts.ContainsIdentifier)) == 0)
        {
            return node;
        }

        switch (node.Kind)
        {
            case Kind.SourceFile:
                node = VisitSourceFile(node.AsSourceFile()!)!;
                break;
            case Kind.ForStatement:
                node = VisitForStatement(node.AsForStatement()!)!;
                break;
            case Kind.ForInStatement:
            case Kind.ForOfStatement:
                node = VisitForInOrOfStatement(node.AsForInOrOfStatement()!)!;
                break;
            case Kind.ExpressionStatement:
                node = VisitExpressionStatement(node.AsExpressionStatement()!)!;
                break;
            case Kind.VoidExpression:
                node = VisitVoidExpression(node.AsVoidExpression()!)!;
                break;
            case Kind.ParenthesizedExpression:
                node = VisitParenthesizedExpression(node.AsParenthesizedExpression()!, resultIsDiscarded)!;
                break;
            case Kind.PartiallyEmittedExpression:
                node = VisitPartiallyEmittedExpression(node.AsPartiallyEmittedExpression()!, resultIsDiscarded)!;
                break;
            case Kind.CallExpression:
                node = VisitCallExpression(node.AsCallExpression()!)!;
                break;
            case Kind.TaggedTemplateExpression:
                node = VisitTaggedTemplateExpression(node.AsTaggedTemplateExpression()!)!;
                break;
            case Kind.BinaryExpression:
                node = VisitBinaryExpression(node.AsBinaryExpression()!, resultIsDiscarded)!;
                break;
            case Kind.PrefixUnaryExpression:
                node = VisitPrefixUnaryExpression(node.AsPrefixUnaryExpression()!, resultIsDiscarded)!;
                break;
            case Kind.PostfixUnaryExpression:
                node = VisitPostfixUnaryExpression(node.AsPostfixUnaryExpression()!, resultIsDiscarded)!;
                break;
            case Kind.ShorthandPropertyAssignment:
                node = VisitShorthandPropertyAssignment(node.AsShorthandPropertyAssignment()!)!;
                break;
            case Kind.Identifier:
                node = VisitIdentifier(node)!;
                break;
            default:
                node = Visitor().VisitEachChild(node)!;
                break;
        }

        return node;
    }

    // Visits source elements whose value is discarded if they are expressions.
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitDiscardedValue(node *ast.Node) *ast.Node</c>。</summary>
    internal Node? VisitDiscardedValue(Node node)
    {
        var grandparentNode = PushNode(node);
        try
        {
            return VisitNoStack(node, true /*resultIsDiscarded*/);
        }
        finally
        {
            PopNode(grandparentNode);
        }
    }

    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitAssignmentPattern(node *ast.Node) *ast.Node</c>。</summary>
    internal Node? VisitAssignmentPattern(Node node)
    {
        var grandparentNode = PushNode(node);
        try
        {
            return VisitAssignmentPatternNoStack(node);
        }
        finally
        {
            PopNode(grandparentNode);
        }
    }

    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitAssignmentPatternNoStack(node *ast.Node) *ast.Node</c>。</summary>
    private Node? VisitAssignmentPatternNoStack(Node node)
    {
        switch (node.Kind)
        {
            // AssignmentPattern
            case Kind.ObjectLiteralExpression:
            case Kind.ArrayLiteralExpression:
                node = _assignmentPatternVisitor.VisitEachChild(node)!;
                break;

            // AssignmentProperty
            case Kind.PropertyAssignment:
                node = VisitAssignmentProperty(node.AsPropertyAssignment()!)!;
                break;
            case Kind.ShorthandPropertyAssignment:
                node = VisitShorthandAssignmentProperty(node.AsShorthandPropertyAssignment()!)!;
                break;

            // AssignmentRestProperty
            case Kind.SpreadAssignment:
                node = VisitAssignmentRestProperty(node.AsSpreadAssignment()!)!;
                break;

            // AssignmentRestElement
            case Kind.SpreadElement:
                node = VisitAssignmentRestElement(node.AsSpreadElement()!)!;
                break;

            // AssignmentElement
            default:
                if (IsExpression(node))
                {
                    node = VisitAssignmentElement(node)!;
                    break;
                }

                node = VisitNoStack(node, false /*resultIsDiscarded*/)!;
                break;
        }
        return node;
    }

    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitSourceFile(node *ast.SourceFile) *ast.Node</c>。</summary>
    private Node VisitSourceFile(SourceFile node)
    {
        if (node.IsDeclarationFile ||
            !(IsEffectiveExternalModule(node, _compilerOptions) ||
                (node.SubtreeFacts & SubtreeFacts.ContainsDynamicImport) != 0))
        {
            return node;
        }

        _currentSourceFile = node;
        _currentModuleInfo = ModuleTransforms.CollectExternalModuleInfo(node, _compilerOptions, EmitContext(), _resolver);
        var updated = TransformCommonJSModule(node);
        _currentSourceFile = null;
        _currentModuleInfo = null;
        return updated;
    }

    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) shouldEmitUnderscoreUnderscoreESModule() bool</c>。</summary>
    private bool ShouldEmitUnderscoreUnderscoreESModule()
    {
        if (FileExtensionIsOneOf(_currentSourceFile!.FileName(), SupportedJSExtensionsFlat) &&
            _currentSourceFile.CommonJSModuleIndicator is not null &&
            (_currentSourceFile.ExternalModuleIndicator is null || _currentSourceFile.ExternalModuleIndicator.Kind == Kind.SourceFile))
        {
            return false;
        }
        if (_currentModuleInfo!._exportEquals is null && IsExternalModule(_currentSourceFile))
        {
            return true;
        }
        return false;
    }

    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) createUnderscoreUnderscoreESModule() *ast.Statement</c>。</summary>
    private Node CreateUnderscoreUnderscoreESModule()
    {
        var statement = Factory().NewExpressionStatement(
            Factory().NewCallExpression(
                Factory().NewPropertyAccessExpression(
                    Factory().NewIdentifier("Object"),
                    null, /*questionDotToken*/
                    Factory().NewIdentifier("defineProperty"),
                    NodeFlags.None),
                null, /*questionDotToken*/
                null, /*typeArguments*/
                Factory().NewNodeList([
                    Factory().NewIdentifier("exports"),
                    Factory().NewStringLiteral("__esModule", TokenFlags.None),
                    Factory().NewObjectLiteralExpression(
                        Factory().NewNodeList([
                            Factory().NewPropertyAssignment(
                                null, /*modifiers*/
                                Factory().NewIdentifier("value"),
                                null, /*postfixToken*/
                                null, /*typeNode*/
                                Factory().NewTrueExpression()),
                        ]),
                        false /*multiLine*/),
                ]),
                NodeFlags.None));
        EmitContext().SetEmitFlags(statement, EmitFlags.CustomPrologue);
        return statement;
    }

    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) transformCommonJSModule(node *ast.SourceFile) *ast.Node</c>。</summary>
    private Node TransformCommonJSModule(SourceFile node)
    {
        EmitContext().StartVariableEnvironment();

        // emit standard prologue directives (e.g. "use strict")
        var (prologue, rest) = Factory().SplitStandardPrologue(node.Statements!.Nodes ?? []);
        var statements = new List<Node>(prologue);

        // emit custom prologues from other transformations
        var (custom, restAfterCustom) = Factory().SplitCustomPrologue(rest ?? []);
        statements.AddRange(_topLevelVisitor.VisitSlice(custom).Result ?? []);

        // emits `Object.defineProperty(exports, "__esModule", { value: true });` at the top of the file
        if (ShouldEmitUnderscoreUnderscoreESModule())
        {
            statements.Add(CreateUnderscoreUnderscoreESModule());
        }

        // initialize all exports to `undefined`, e.g.:
        //  exports.a = exports.b = void 0;
        if (_currentModuleInfo!._exportedNames.Count > 0)
        {
            const int chunkSize = 50;
            var l = _currentModuleInfo._exportedNames.Count;
            for (var i = 0; i < l; i += chunkSize)
            {
                var right = Factory().NewVoidZeroExpression();
                // Go: `exportedNames[i:min(i+chunkSize, l)]`（切片视图）→ C# 按相同边界遍历。
                for (var j = i; j < Math.Min(i + chunkSize, l); j++)
                {
                    var nextId = _currentModuleInfo._exportedNames[j];
                    Node left;
                    if (nextId.Kind == Kind.StringLiteral)
                    {
                        left = Factory().NewElementAccessExpression(
                            Factory().NewIdentifier("exports"),
                            null, /*questionDotToken*/
                            Factory().NewStringLiteralFromNode(nextId),
                            NodeFlags.None);
                    }
                    else
                    {
                        var name = nextId.Clone(Factory())!;
                        EmitContext().SetEmitFlags(name, EmitFlags.NoSourceMap | EmitFlags.NoComments);
                        left = Factory().NewPropertyAccessExpression(
                            Factory().NewIdentifier("exports"),
                            null, /*questionDotToken*/
                            name,
                            NodeFlags.None);
                    }
                    right = Factory().NewAssignmentExpression(left, right);
                }
                var statement = Factory().NewExpressionStatement(right);
                EmitContext().AddEmitFlags(statement, EmitFlags.CustomPrologue);
                statements.Add(statement);
            }
        }

        // initialize exports for function declarations, e.g.:
        //  exports.f = f;
        //  function f() {}
        // These are marked as custom prologue so they are ordered before the external helpers
        // import declaration (e.g., `const tslib_1 = require("tslib")`), matching TypeScript's emit order.
        var exportedFunctionsStart = statements.Count;
        foreach (var f in _currentModuleInfo._exportedFunctions.Values())
        {
            AppendExportsOfClassOrFunctionDeclaration(statements, f.AsNode()!);
        }
        for (var i = exportedFunctionsStart; i < statements.Count; i++)
        {
            EmitContext().AddEmitFlags(statements[i], EmitFlags.CustomPrologue);
        }

        // visit the remaining statements in the source file
        statements.AddRange(_topLevelVisitor.VisitSlice(restAfterCustom).Result ?? []);

        // emit `module.exports = ...` if needd
        statements = AppendExportEqualsIfNeeded(statements);

        // merge temp variables into the statement list
        var mergedStatements = EmitContext().EndAndMergeVariableEnvironment(statements.ToArray()) ?? [];
        var statementList = Factory().NewNodeList(mergedStatements);
        statementList.Loc = node.Statements!.Loc;
        var result = Factory().UpdateSourceFile(node, statementList, node.EndOfFileToken).AsSourceFile()!;
        EmitContext().AddEmitHelper(result.AsNode(), EmitContext().ReadEmitHelpers());

        var externalHelpersImportDeclaration = ModuleTransforms.CreateExternalHelpersImportDeclarationIfNeeded(EmitContext(), result, _compilerOptions, _getEmitModuleFormatOfFile!(node), false /*hasExportStarsToExportValues*/, false /*hasImportStar*/, false /*hasImportDefault*/);
        if (externalHelpersImportDeclaration is not null)
        {
            // Go 在这里重新 `:=` 声明 prologue/rest（遮蔽外层）→ C# 用不同名字保持一致语义。
            var (prologue2, rest2) = Factory().SplitStandardPrologue(result.Statements!.Nodes ?? []);
            var (custom2, restAfterCustom2) = Factory().SplitCustomPrologue(rest2 ?? []);
            var statements2 = new List<Node>(prologue2);
            statements2.AddRange(custom2 ?? []);
            statements2.Add(_topLevelVisitor.VisitNode(externalHelpersImportDeclaration)!);
            statements2.AddRange(restAfterCustom2);
            var statementList2 = Factory().NewNodeList(statements2.ToArray());
            statementList2.Loc = result.Statements!.Loc;
            result = Factory().UpdateSourceFile(result, statementList2, node.EndOfFileToken).AsSourceFile()!;
        }

        return result;
    }

    // Adds the down-level representation of `export=` to the statement list if one exists in the source file.
    //
    // - The `statements` parameter is a statement list to which the down-level export statements are to be appended.
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) appendExportEqualsIfNeeded(statements []*ast.Statement) []*ast.Statement</c>。</summary>
    private List<Node> AppendExportEqualsIfNeeded(List<Node> statements)
    {
        if (_currentModuleInfo!._exportEquals is not null)
        {
            var expressionResult = VisitExportEquals(_currentModuleInfo._exportEquals.AsExportAssignment()!);
            if (expressionResult is not null)
            {
                var statement = Factory().NewExpressionStatement(
                    Factory().NewAssignmentExpression(
                        Factory().NewPropertyAccessExpression(
                            Factory().NewIdentifier("module"),
                            null, /*questionDotToken*/
                            Factory().NewIdentifier("exports"),
                            NodeFlags.None),
                        expressionResult));

                EmitContext().AssignCommentAndSourceMapRanges(statement, _currentModuleInfo._exportEquals.AsNode());
                EmitContext().AddEmitFlags(statement, EmitFlags.NoComments);
                statements.Add(statement);
            }
        }
        return statements;
    }

    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) visitExportEquals(node *ast.ExportAssignment) *ast.Expression</c>。</summary>
    private Node? VisitExportEquals(ExportAssignment node)
    {
        var grandparentNode = PushNode(node.AsNode());
        try
        {
            return Visitor().VisitNode(node.Expression);
        }
        finally
        {
            PopNode(grandparentNode);
        }
    }

    // Appends the exports of an ImportDeclaration to a statement list, returning the statement list.
    //
    //   - The `statements` parameter is a statement list to which the down-level export statements are to be appended.
    //   - The `decl` parameter is the declaration whose exports are to be recorded.
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) appendExportsOfImportDeclaration(statements []*ast.Statement, decl *ast.ImportDeclaration) []*ast.Statement</c>。</summary>
    private List<Node> AppendExportsOfImportDeclaration(List<Node> statements, ImportDeclaration decl)
    {
        if (_currentModuleInfo!._exportEquals is not null)
        {
            return statements;
        }

        var importClause = decl.ImportClause;
        if (importClause is null)
        {
            return statements;
        }

        var seen = new Set<string>();
        if (importClause.Name() is not null)
        {
            statements = AppendExportsOfDeclaration(statements, importClause, seen, false /*liveBinding*/);
        }

        var namedBindings = importClause.AsImportClause()!.NamedBindings;
        if (namedBindings is not null)
        {
            switch (namedBindings.Kind)
            {
                case Kind.NamespaceImport:
                    statements = AppendExportsOfDeclaration(statements, namedBindings, seen, false /*liveBinding*/);
                    break;

                case Kind.NamedImports:
                    foreach (var importBinding in namedBindings.Elements() ?? [])
                    {
                        statements = AppendExportsOfDeclaration(statements, importBinding, seen, true /*liveBinding*/);
                    }
                    break;
            }
        }

        return statements;
    }

    // Appends the exports of a VariableStatement to a statement list, returning the statement list.
    //
    //   - The `statements` parameter is a statement list to which the down-level export statements are to be appended.
    //   - The `node` parameter is the VariableStatement whose exports are to be recorded.
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) appendExportsOfVariableStatement(statements []*ast.Statement, node *ast.VariableStatement) []*ast.Statement</c>。</summary>
    private List<Node> AppendExportsOfVariableStatement(List<Node> statements, VariableStatement node)
    {
        return AppendExportsOfVariableDeclarationList(statements, node.DeclarationList!.AsVariableDeclarationList()! /*isForInOrOfInitializer*/, false);
    }

    // Appends the exports of a VariableDeclarationList to a statement list, returning the statement list.
    //
    //   - The `statements` parameter is a statement list to which the down-level export statements are to be appended.
    //   - The `node` parameter is the VariableDeclarationList whose exports are to be recorded.
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) appendExportsOfVariableDeclarationList(statements []*ast.Statement, node *ast.VariableDeclarationList, isForInOrOfInitializer bool) []*ast.Statement</c>。</summary>
    private List<Node> AppendExportsOfVariableDeclarationList(List<Node> statements, VariableDeclarationList node, bool isForInOrOfInitializer)
    {
        if (_currentModuleInfo!._exportEquals is not null)
        {
            return statements;
        }

        foreach (var decl in node.Declarations!.Nodes ?? [])
        {
            statements = AppendExportsOfBindingElement(statements, decl, isForInOrOfInitializer);
        }

        return statements;
    }

    // Appends the exports of a VariableDeclaration or BindingElement to a statement list, returning the statement list.
    //
    //   - The `statements` parameter is a statement list to which the down-level export statements are to be appended.
    //   - The `decl` parameter is the declaration whose exports are to be recorded.
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) appendExportsOfBindingElement(statements []*ast.Statement, decl *ast.Node, isForInOrOfInitializer bool) []*ast.Statement</c>。</summary>
    private List<Node> AppendExportsOfBindingElement(List<Node> statements, Node decl /*VariableDeclaration | BindingElement*/, bool isForInOrOfInitializer)
    {
        if (_currentModuleInfo!._exportEquals is not null || decl.Name() is null)
        {
            return statements;
        }

        if (IsBindingPattern(decl.Name()))
        {
            foreach (var element in decl.Name()!.Elements() ?? [])
            {
                if (!IsOmittedExpression(element))
                {
                    statements = AppendExportsOfBindingElement(statements, element, isForInOrOfInitializer);
                }
            }
        }
        else if (!IsGeneratedIdentifier(EmitContext(), decl.Name()!) &&
            (!IsVariableDeclaration(decl) || decl.AsVariableDeclaration()!.Initializer is not null || isForInOrOfInitializer))
        {
            statements = AppendExportsOfDeclaration(statements, decl, null /*seen*/, false /*liveBinding*/);
        }

        return statements;
    }

    // Appends the exports of a ClassDeclaration or FunctionDeclaration to a statement list, returning the statement list.
    //
    //   - The `statements` parameter is a statement list to which the down-level export statements are to be appended.
    //   - The `decl` parameter is the declaration whose exports are to be recorded.
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) appendExportsOfClassOrFunctionDeclaration(statements []*ast.Statement, decl *ast.Declaration) []*ast.Statement</c>。</summary>
    private List<Node> AppendExportsOfClassOrFunctionDeclaration(List<Node> statements, Node decl)
    {
        if (_currentModuleInfo!._exportEquals is not null)
        {
            return statements;
        }

        var seen = new Set<string>();
        if (HasSyntacticModifier(decl, ModifierFlags.Export))
        {
            Node exportName;
            if (HasSyntacticModifier(decl, ModifierFlags.Default))
            {
                exportName = Factory().NewIdentifier("default");
            }
            else
            {
                exportName = Factory().GetDeclarationName(decl);
            }

            var exportValue = Factory().GetLocalName(decl);
            statements = AppendExportStatement(statements, seen, exportName, exportValue, decl.Loc, false /*allowComments*/, false /*liveBinding*/);
        }

        if (decl.Name() is not null)
        {
            return AppendExportsOfDeclaration(statements, decl, seen, false /*liveBinding*/);
        }

        return statements;
    }

    // Appends the exports of a declaration to a statement list, returning the statement list.
    //
    //   - The `statements` parameter is a statement list to which the down-level export statements are to be appended.
    //   - The `decl` parameter is the declaration to export.
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) appendExportsOfDeclaration(statements []*ast.Statement, decl *ast.Declaration, seen *collections.Set[string], liveBinding bool) []*ast.Statement</c>。</summary>
    private List<Node> AppendExportsOfDeclaration(List<Node> statements, Node decl, Set<string>? seen, bool liveBinding)
    {
        if (_currentModuleInfo!._exportEquals is not null)
        {
            return statements;
        }

        seen ??= new Set<string>();

        var name = decl.Name();
        if (_currentModuleInfo._exportSpecifiers.Len() > 0 && name is not null && IsIdentifier(name))
        {
            name = Factory().GetDeclarationName(decl);
            var exportSpecifiers = _currentModuleInfo._exportSpecifiers.Get(name.Text());
            if (exportSpecifiers is not null && exportSpecifiers.Count > 0)
            {
                var exportValue = VisitExpressionIdentifier(name)!;
                foreach (var exportSpecifier in exportSpecifiers)
                {
                    statements = AppendExportStatement(statements, seen, exportSpecifier.Name()!, exportValue, exportSpecifier.Name()!.Loc /*location*/, false /*allowComments*/, liveBinding);
                }
            }
        }

        return statements;
    }

    // Appends the down-level representation of an export to a statement list, returning the statement list.
    //
    //   - The `statements` parameter is a statement list to which the down-level export statements are to be appended.
    //   - The `exportName` parameter is the name of the export.
    //   - The `expression` parameter is the expression to export.
    //   - The `location` parameter is the location to use for source maps and comments for the export.
    //   - The `allowComments` parameter indicates whether to allow comments on the export.
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) appendExportStatement(statements []*ast.Statement, seen *collections.Set[string], exportName *ast.ModuleExportName, expression *ast.Expression, location *core.TextRange, allowComments bool, liveBinding bool) []*ast.Statement</c>。</summary>
    private List<Node> AppendExportStatement(List<Node> statements, Set<string> seen, Node exportName, Node expression, TextRange? location, bool allowComments, bool liveBinding)
    {
        if (exportName.Kind != Kind.StringLiteral)
        {
            if (seen.Has(exportName.Text()))
            {
                return statements;
            }
            seen.Add(exportName.Text());
        }
        statements.Add(CreateExportStatement(exportName, expression, location, allowComments, liveBinding));
        return statements;
    }

    // Creates a call to the current file's export function to export a value.
    //
    //   - The `name` parameter is the bound name of the export.
    //   - The `value` parameter is the exported value.
    //   - The `location` parameter is the location to use for source maps and comments for the export.
    //   - The `allowComments` parameter indicates whether to emit comments for the statement.
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) createExportStatement(name *ast.ModuleExportName, value *ast.Expression, location *core.TextRange, allowComments bool, liveBinding bool) *ast.Statement</c>。</summary>
    private Node CreateExportStatement(Node name, Node value, TextRange? location, bool allowComments, bool liveBinding)
    {
        var statement = Factory().NewExpressionStatement(CreateExportExpression(name, value, null /*location*/, liveBinding));
        if (location is not null)
        {
            EmitContext().SetCommentRange(statement, location.Value);
        }
        EmitContext().AddEmitFlags(statement, EmitFlags.StartOnNewLine);
        if (!allowComments)
        {
            EmitContext().AddEmitFlags(statement, EmitFlags.NoComments);
        }
        return statement;
    }

    // Creates a call to the current file's export function to export a value.
    //
    //   - The `name` parameter is the bound name of the export.
    //   - The `value` parameter is the exported value.
    //   - The `location` parameter is the location to use for source maps and comments for the export.
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) createExportExpression(name *ast.ModuleExportName, value *ast.Expression, location *core.TextRange, liveBinding bool) *ast.Expression</c>。</summary>
    private Node CreateExportExpression(Node name, Node value, TextRange? location, bool liveBinding)
    {
        Node expression;
        if (liveBinding)
        {
            // For a live binding we emit a getter on `exports` that returns the value:
            //  Object.defineProperty(exports, "<name>", { enumerable: true, get: function () { return <value>; } });
            expression = Factory().NewCallExpression(
                Factory().NewPropertyAccessExpression(
                    Factory().NewIdentifier("Object"),
                    null, /*questionDotToken*/
                    Factory().NewIdentifier("defineProperty"),
                    NodeFlags.None),
                null, /*questionDotToken*/
                null, /*typeArguments*/
                Factory().NewNodeList([
                    Factory().NewIdentifier("exports"),
                    Factory().NewStringLiteralFromNode(name),
                    Factory().NewObjectLiteralExpression(
                        Factory().NewNodeList([
                            Factory().NewPropertyAssignment(
                                null, /*modifiers*/
                                Factory().NewIdentifier("enumerable"),
                                null, /*postfixToken*/
                                null, /*typeNode*/
                                Factory().NewTrueExpression()),
                            Factory().NewPropertyAssignment(
                                null, /*modifiers*/
                                Factory().NewIdentifier("get"),
                                null, /*postfixToken*/
                                null, /*typeNode*/
                                Factory().NewFunctionExpression(
                                    null, /*modifiers*/
                                    null, /*asteriskToken*/
                                    null, /*name*/
                                    null, /*typeParameters*/
                                    Factory().NewNodeList([]),
                                    null, /*type*/
                                    null, /*fullSignature*/
                                    Factory().NewBlock(
                                        Factory().NewNodeList([
                                            Factory().NewReturnStatement(value),
                                        ]),
                                        false /*multiLine*/))),
                        ]),
                        false /*multiLine*/),
                ]),
                NodeFlags.None);
        }
        else
        {
            // Otherwise, we emit a simple property assignment.
            Node left;
            if (name.Kind == Kind.StringLiteral)
            {
                // emits:
                //  exports["<name>"] = <value>;
                left = Factory().NewElementAccessExpression(
                    Factory().NewIdentifier("exports"),
                    null, /*questionDotToken*/
                    Factory().NewStringLiteralFromNode(name),
                    NodeFlags.None);
            }
            else
            {
                // emits:
                //  exports.<name> = <value>;
                left = Factory().NewPropertyAccessExpression(
                    Factory().NewIdentifier("exports"),
                    null, /*questionDotToken*/
                    name.Clone(Factory())!,
                    NodeFlags.None);
            }
            expression = Factory().NewAssignmentExpression(left, value);
        }
        if (location is not null)
        {
            EmitContext().SetCommentRange(expression, location.Value);
        }
        return expression;
    }

    // Creates a `require()` call to import an external module.
    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) createRequireCall(node *ast.Node) *ast.Node</c>。</summary>
    private Node CreateRequireCall(Node node /*ImportDeclaration | ImportEqualsDeclaration | ExportDeclaration*/)
    {
        var args = new List<Node>();
        var moduleName = ModuleTransforms.GetExternalModuleNameLiteral(Factory(), node, _currentSourceFile!, null /*host*/, null /*resolver*/, _compilerOptions);
        if (moduleName is not null)
        {
            args.Add(ModuleTransforms.RewriteModuleSpecifier(EmitContext(), moduleName, _compilerOptions)!);
        }
        return Factory().NewCallExpression(
            Factory().NewIdentifier("require"),
            null, /*questionDotToken*/
            null, /*typeArguments*/
            Factory().NewNodeList(args.ToArray()),
            NodeFlags.None);
    }

    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) getHelperExpressionForExport(node *ast.ExportDeclaration, innerExpr *ast.Expression) *ast.Expression</c>。</summary>
    private Node GetHelperExpressionForExport(ExportDeclaration node, Node innerExpr)
    {
        if (ModuleTransforms.GetExportNeedsImportStarHelper(node))
        {
            return Visitor().VisitNode(Factory().NewImportStarHelper(innerExpr))!;
        }
        return innerExpr;
    }

    /// <summary>Go: <c>func (tx *CommonJSModuleTransformer) getHelperExpressionForImport(node *ast.ImportDeclaration, innerExpr *ast.Expression) *ast.Expression</c>。</summary>
    private Node GetHelperExpressionForImport(ImportDeclaration node, Node innerExpr)
    {
        if (ModuleTransforms.GetImportNeedsImportStarHelper(node))
        {
            return Visitor().VisitNode(Factory().NewImportStarHelper(innerExpr))!;
        }
        if (ModuleTransforms.GetImportNeedsImportDefaultHelper(node))
        {
            return Visitor().VisitNode(Factory().NewImportDefaultHelper(innerExpr))!;
        }
        return innerExpr;
    }
}

/// <summary>Go: moduletransforms 包的包级导出处方函数。</summary>
public static partial class ModuleTransforms
{
    /// <summary>Go: <c>func NewCommonJSModuleTransformer(opts *transformers.TransformOptions) *transformers.Transformer</c>。</summary>
    public static Transformer? NewCommonJSModuleTransformer(TransformOptions opts)
    {
        var compilerOptions = opts.CompilerOptions;
        var emitContext = opts.Context;
        var tx = new CommonJSModuleTransformer
        {
            _compilerOptions = compilerOptions!,
            _resolver = opts.Resolver,
            _getEmitModuleFormatOfFile = opts.GetEmitModuleFormatOfFile,
        };
        tx._topLevelVisitor = emitContext!.NewNodeVisitor(tx.VisitTopLevel);
        tx._topLevelNestedVisitor = emitContext.NewNodeVisitor(tx.VisitTopLevelNested);
        tx._discardedValueVisitor = emitContext.NewNodeVisitor(tx.VisitDiscardedValue);
        tx._assignmentPatternVisitor = emitContext.NewNodeVisitor(tx.VisitAssignmentPattern);
        tx._languageVersion = compilerOptions!.GetEmitScriptTarget();
        tx._moduleKind = compilerOptions.GetEmitModuleKind();
        return tx.NewTransformer(tx.Visit, emitContext);
    }
}
