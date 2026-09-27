// ported from: internal/transformers/estransforms/forawait.go:1-857（for-await-of / async generator 降级：
//              `__asyncValues`/`__asyncDelegator`/`__asyncGenerator` 包裹、迭代器 try/catch/finally 展开；
//              含文件末尾的包级辅助函数 forawait.go:308-317）
//
// 与 Go 的差异（C# 语言特性，逐条）：
// 1) Go 用**值内嵌** `superAccessState`；C# 已继承 `Transformer`（无多继承），改为**组合**字段 `_super`
//    （类型 `SuperAccessState` 见 estransforms/utilities.go 的移植，由 T5 落地）。调用点见 Async.cs 文件头差异 1。
// 2) Go 的 `type forAwaitHierarchyFacts int` + `1 << iota` 常量 → `[Flags] enum ForAwaitHierarchyFacts`
//    （数值与 Go 一致；`forAwaitHierarchyFactsAncestorFactsMask = 1<<iota - 1` 在 iota=2 处 = 3）。
//    别名常量（SourceFileExcludes 等）按 Go 逐条给出显式取值，保持与 Go 同名。
// 3) Go 的 `doWithHierarchyFacts(cb func(*forawaitTransformer, *ast.Node) *ast.Node, ...)` → `Func<Node, Node?>` 方法组。
// 4) Go 的 `node.Parameters()`/`node.Statements()`（`*Node` 上的访问器）在 C# 没有对应方法（docs/ast.md §2），
//    本文件用私有静态 `Parameters(Node)` 包装（NodeList 为 nil 时 Go panic、C# NRE，语义相同）。
// 5) `[]*ast.Node` + `append` → `List<Node>` + `ToArray()`（docs/architecture.md §3.2：C# 数组没有 len<cap 视图）。
// 6) 包级函数（Go 无接收者）→ 与包同名的 `public static partial class EsTransforms`（docs/architecture.md §2）：
//    本文件里是 forawait.go 的 `unwrapInnermostStatementOfLabel` + `newforawaitTransformer`。
using System;
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.Printer;
using static TypeScript.Ast.Ast;
using static TypeScript.Printer.Printer;
using static TypeScript.Transformers.EsTransforms.EsTransforms;
using static TypeScript.Transformers.Transformers;

namespace TypeScript.Transformers.EsTransforms;

/// <summary>Go: <c>type forAwaitHierarchyFacts int</c>（forawait.go:12-40；Facts we track as we traverse the tree）。</summary>
[Flags]
internal enum ForAwaitHierarchyFacts
{
    /// <summary>Go: <c>forAwaitHierarchyFactsNone</c>。</summary>
    None = 0,

    //
    // Ancestor facts
    //

    /// <summary>Go: <c>forAwaitHierarchyFactsHasLexicalThis</c>（1 &lt;&lt; 0）。</summary>
    HasLexicalThis = 1 << 0,
    /// <summary>Go: <c>forAwaitHierarchyFactsIterationContainer</c>（1 &lt;&lt; 1）。</summary>
    IterationContainer = 1 << 1,

    //
    // Ancestor masks
    //

    /// <summary>Go: <c>forAwaitHierarchyFactsAncestorFactsMask</c>（1&lt;&lt;iota - 1，iota=2 → 3）。</summary>
    AncestorFactsMask = (1 << 2) - 1,

    /// <summary>Go: <c>forAwaitHierarchyFactsSourceFileExcludes</c>（= IterationContainer）。</summary>
    SourceFileExcludes = IterationContainer,
    /// <summary>Go: <c>forAwaitHierarchyFactsStrictModeSourceFileIncludes</c>（= None）。</summary>
    StrictModeSourceFileIncludes = None,

    /// <summary>Go: <c>forAwaitHierarchyFactsClassOrFunctionIncludes</c>（= HasLexicalThis）。</summary>
    ClassOrFunctionIncludes = HasLexicalThis,
    /// <summary>Go: <c>forAwaitHierarchyFactsClassOrFunctionExcludes</c>（= IterationContainer）。</summary>
    ClassOrFunctionExcludes = IterationContainer,

    /// <summary>Go: <c>forAwaitHierarchyFactsArrowFunctionIncludes</c>（= None）。</summary>
    ArrowFunctionIncludes = None,
    /// <summary>Go: <c>forAwaitHierarchyFactsArrowFunctionExcludes</c>（= ClassOrFunctionExcludes = IterationContainer）。</summary>
    ArrowFunctionExcludes = ClassOrFunctionExcludes,

    /// <summary>Go: <c>forAwaitHierarchyFactsIterationStatementIncludes</c>（= IterationContainer）。</summary>
    IterationStatementIncludes = IterationContainer,
    /// <summary>Go: <c>forAwaitHierarchyFactsIterationStatementExcludes</c>（= None）。</summary>
    IterationStatementExcludes = None,
}

/// <summary>Go: <c>type forawaitTransformer struct</c>（forawait.go:42-54）。</summary>
internal sealed class ForAwaitTransformer : Transformer
{
    /// <summary>Go: <c>compilerOptions</c>（Go 只赋值不读取，字段保留以对齐结构）。</summary>
    internal CompilerOptions? _compilerOptions;

    /// <summary>Go: 内嵌字段 <c>superAccessState</c>（见文件头差异 1）。</summary>
    internal SuperAccessState _super = new();

    private FunctionFlags _enclosingFunctionFlags;
    private ForAwaitHierarchyFacts _forAwaitHierarchyFacts;

    /// <summary>Go: <c>exportedVariableStatement</c>（Go 只赋值不读取，字段保留以对齐结构）。</summary>
    internal bool _exportedVariableStatement;

    internal NodeVisitor? _fallbackNodeVisitor;
    internal NodeVisitor? _noAsyncModifierVisitor;

    /// <summary>Go: <c>node.Parameters()</c>（见文件头差异 4）。</summary>
    private static Node[] Parameters(Node node) => node.ParameterList()!.Nodes!;

    /// <summary>Go: <c>func (tx *forawaitTransformer) affectsSubtree</c>（forawait.go:64-66）。</summary>
    private bool AffectsSubtree(ForAwaitHierarchyFacts excludeFacts, ForAwaitHierarchyFacts includeFacts) =>
        _forAwaitHierarchyFacts != ((_forAwaitHierarchyFacts & ~excludeFacts) | includeFacts);

    /// <summary>
    /// Go: <c>func (tx *forawaitTransformer) enterSubtree</c>（forawait.go:68-73）。
    /// sets the HierarchyFacts for this node prior to visiting this node's subtree,
    /// returning the facts set prior to modification.
    /// </summary>
    private ForAwaitHierarchyFacts EnterSubtree(ForAwaitHierarchyFacts excludeFacts, ForAwaitHierarchyFacts includeFacts)
    {
        var ancestorFacts = _forAwaitHierarchyFacts;
        _forAwaitHierarchyFacts = ((_forAwaitHierarchyFacts & ~excludeFacts) | includeFacts) & ForAwaitHierarchyFacts.AncestorFactsMask;
        return ancestorFacts;
    }

    /// <summary>
    /// Go: <c>func (tx *forawaitTransformer) exitSubtree</c>（forawait.go:75-78）。
    /// restores the HierarchyFacts for this node's ancestor after visiting this node's subtree.
    /// </summary>
    private void ExitSubtree(ForAwaitHierarchyFacts ancestorFacts)
    {
        _forAwaitHierarchyFacts = ancestorFacts;
    }

    /// <summary>Go: <c>func (tx *forawaitTransformer) visitModifiersNoAsync</c>（forawait.go:80-82）。</summary>
    private ModifierList? VisitModifiersNoAsync(ModifierList? modifiers) => _noAsyncModifierVisitor!.VisitModifiers(modifiers);

    /// <summary>Go: <c>func (tx *forawaitTransformer) doWithHierarchyFacts</c>（forawait.go:84-93）。</summary>
    private Node? DoWithHierarchyFacts(Func<Node, Node?> cb, Node node, ForAwaitHierarchyFacts excludeFacts, ForAwaitHierarchyFacts includeFacts)
    {
        if (AffectsSubtree(excludeFacts, includeFacts))
        {
            var ancestorFacts = EnterSubtree(excludeFacts, includeFacts);
            var result = cb(node);
            ExitSubtree(ancestorFacts);
            return result;
        }
        return cb(node);
    }

    /// <summary>Go: <c>func (tx *forawaitTransformer) visitDefault</c>（forawait.go:95-97）。</summary>
    private Node? VisitDefault(Node node) => Visitor().VisitEachChild(node);

    /// <summary>Go: <c>func (tx *forawaitTransformer) fallbackVisitor</c>（forawait.go:99-113）。</summary>
    private Node? FallbackVisitor(Node node)
    {
        if (_super._capturedSuperProperties is null)
        {
            return node;
        }
        switch (node.Kind)
        {
            case Kind.FunctionExpression:
            case Kind.FunctionDeclaration:
            case Kind.MethodDeclaration:
            case Kind.GetAccessor:
            case Kind.SetAccessor:
            case Kind.Constructor:
                return node;
        }
        _super.TrackSuperAccess(node);
        return _fallbackNodeVisitor!.VisitEachChild(node);
    }

    /// <summary>Go: <c>func (tx *forawaitTransformer) visitFallback</c>（forawait.go:115-117）。</summary>
    internal Node? VisitFallback(Node node) => FallbackVisitor(node);

    /// <summary>Go: <c>func (tx *forawaitTransformer) visit</c>（forawait.go:119-196）。工厂方法用方法组传入 → internal。</summary>
    internal Node? Visit(Node node)
    {
        if ((node.SubtreeFacts & SubtreeFacts.ContainsForAwaitOrAsyncGenerator) == 0)
        {
            return FallbackVisitor(node);
        }
        _super.TrackSuperAccess(node);
        switch (node.Kind)
        {
            case Kind.SourceFile:
                return VisitSourceFile(node.AsSourceFile()!);
            case Kind.AwaitExpression:
                return VisitAwaitExpression(node.AsAwaitExpression()!);
            case Kind.YieldExpression:
                return VisitYieldExpression(node.AsYieldExpression()!);
            case Kind.ReturnStatement:
                return VisitReturnStatement(node.AsReturnStatement()!);
            case Kind.LabeledStatement:
                return VisitLabeledStatement(node.AsLabeledStatement()!);
            case Kind.DoStatement:
            case Kind.WhileStatement:
            case Kind.ForInStatement:
                return DoWithHierarchyFacts(
                    VisitDefault,
                    node,
                    ForAwaitHierarchyFacts.IterationStatementExcludes,
                    ForAwaitHierarchyFacts.IterationStatementIncludes
                );
            case Kind.ForOfStatement:
                return VisitForOfStatement(node.AsForInOrOfStatement()!, null);
            case Kind.ForStatement:
                return DoWithHierarchyFacts(
                    VisitDefault,
                    node,
                    ForAwaitHierarchyFacts.IterationStatementExcludes,
                    ForAwaitHierarchyFacts.IterationStatementIncludes
                );
            case Kind.Constructor:
                return DoWithHierarchyFacts(
                    VisitConstructorDeclaration,
                    node,
                    ForAwaitHierarchyFacts.ClassOrFunctionExcludes,
                    ForAwaitHierarchyFacts.ClassOrFunctionIncludes
                );
            case Kind.MethodDeclaration:
                return DoWithHierarchyFacts(
                    VisitMethodDeclaration,
                    node,
                    ForAwaitHierarchyFacts.ClassOrFunctionExcludes,
                    ForAwaitHierarchyFacts.ClassOrFunctionIncludes
                );
            case Kind.GetAccessor:
                return DoWithHierarchyFacts(
                    VisitGetAccessorDeclaration,
                    node,
                    ForAwaitHierarchyFacts.ClassOrFunctionExcludes,
                    ForAwaitHierarchyFacts.ClassOrFunctionIncludes
                );
            case Kind.SetAccessor:
                return DoWithHierarchyFacts(
                    VisitSetAccessorDeclaration,
                    node,
                    ForAwaitHierarchyFacts.ClassOrFunctionExcludes,
                    ForAwaitHierarchyFacts.ClassOrFunctionIncludes
                );
            case Kind.FunctionDeclaration:
                return DoWithHierarchyFacts(
                    VisitFunctionDeclaration,
                    node,
                    ForAwaitHierarchyFacts.ClassOrFunctionExcludes,
                    ForAwaitHierarchyFacts.ClassOrFunctionIncludes
                );
            case Kind.FunctionExpression:
                return DoWithHierarchyFacts(
                    VisitFunctionExpression,
                    node,
                    ForAwaitHierarchyFacts.ClassOrFunctionExcludes,
                    ForAwaitHierarchyFacts.ClassOrFunctionIncludes
                );
            case Kind.ArrowFunction:
                return DoWithHierarchyFacts(
                    VisitArrowFunction,
                    node,
                    ForAwaitHierarchyFacts.ArrowFunctionExcludes,
                    ForAwaitHierarchyFacts.ArrowFunctionIncludes
                );
            case Kind.ClassDeclaration:
            case Kind.ClassExpression:
                return DoWithHierarchyFacts(
                    VisitDefault,
                    node,
                    ForAwaitHierarchyFacts.ClassOrFunctionExcludes,
                    ForAwaitHierarchyFacts.ClassOrFunctionIncludes
                );
            default:
                return Visitor().VisitEachChild(node);
        }
    }

    /// <summary>Go: <c>func (tx *forawaitTransformer) visitAwaitExpression</c>（forawait.go:198-211）。</summary>
    private Node? VisitAwaitExpression(AwaitExpression node)
    {
        if ((_enclosingFunctionFlags & FunctionFlags.Async) != 0 && (_enclosingFunctionFlags & FunctionFlags.Generator) != 0)
        {
            var result = Factory().NewYieldExpression(
                null, /*asteriskToken*/
                Factory().NewAwaitHelper(Visitor().VisitNode(node.Expression)!)
            );
            result.Loc = node.Loc;
            EmitContext().SetOriginal(result, node);
            return result;
        }
        return Visitor().VisitEachChild(node);
    }

    /// <summary>Go: <c>func (tx *forawaitTransformer) visitYieldExpression</c>（forawait.go:213-257）。</summary>
    private Node? VisitYieldExpression(YieldExpression node)
    {
        if ((_enclosingFunctionFlags & FunctionFlags.Async) != 0 && (_enclosingFunctionFlags & FunctionFlags.Generator) != 0)
        {
            if (node.AsteriskToken is not null)
            {
                var expression = Visitor().VisitNode(node.Expression);

                var asyncValuesResult = Factory().NewAsyncValuesHelper(expression!);
                asyncValuesResult.Loc = expression!.Loc;

                var asyncDelegatorResult = Factory().NewAsyncDelegatorHelper(asyncValuesResult);
                asyncDelegatorResult.Loc = expression.Loc;

                var innerYield = Factory().UpdateYieldExpression(
                    node,
                    node.AsteriskToken,
                    asyncDelegatorResult
                );

                var awaitedYield = Factory().NewAwaitHelper(innerYield);

                var result = Factory().NewYieldExpression(
                    null, /*asteriskToken*/
                    awaitedYield
                );
                result.Loc = node.Loc;
                EmitContext().SetOriginal(result, node);
                return result;
            }

            Node innerExpression;
            if (node.Expression is not null)
            {
                innerExpression = Visitor().VisitNode(node.Expression)!;
            }
            else
            {
                innerExpression = Factory().NewVoidZeroExpression();
            }

            var yieldResult = Factory().NewYieldExpression(
                null, /*asteriskToken*/
                CreateDownlevelAwait(innerExpression)
            );
            yieldResult.Loc = node.Loc;
            EmitContext().SetOriginal(yieldResult, node);
            return yieldResult;
        }

        return Visitor().VisitEachChild(node);
    }

    /// <summary>Go: <c>func (tx *forawaitTransformer) visitReturnStatement</c>（forawait.go:259-273）。</summary>
    private Node? VisitReturnStatement(ReturnStatement node)
    {
        if ((_enclosingFunctionFlags & FunctionFlags.Async) != 0 && (_enclosingFunctionFlags & FunctionFlags.Generator) != 0)
        {
            Node expression;
            if (node.Expression is not null)
            {
                expression = Visitor().VisitNode(node.Expression)!;
            }
            else
            {
                expression = Factory().NewVoidZeroExpression();
            }
            return Factory().UpdateReturnStatement(
                node,
                CreateDownlevelAwait(expression)
            );
        }

        return Visitor().VisitEachChild(node);
    }

    /// <summary>Go: <c>func (tx *forawaitTransformer) visitLabeledStatement</c>（forawait.go:275-290）。</summary>
    private Node? VisitLabeledStatement(LabeledStatement node)
    {
        if ((_enclosingFunctionFlags & FunctionFlags.Async) != 0)
        {
            var statement = UnwrapInnermostStatementOfLabel(node);
            if (statement.Kind == Kind.ForOfStatement && statement.AsForInOrOfStatement()!.AwaitModifier is not null)
            {
                return VisitForOfStatement(statement.AsForInOrOfStatement()!, node);
            }
            return Factory().RestoreEnclosingLabel(Visitor().VisitNode(statement)!, node);
        }
        return Visitor().VisitEachChild(node);
    }

    /// <summary>Go: <c>func (tx *forawaitTransformer) visitSourceFile</c>（forawait.go:319-331）。</summary>
    private Node? VisitSourceFile(SourceFile node)
    {
        var ancestorFacts = EnterSubtree(
            ForAwaitHierarchyFacts.SourceFileExcludes,
            ForAwaitHierarchyFacts.StrictModeSourceFileIncludes
        );
        _exportedVariableStatement = false;
        var visited = Visitor().VisitEachChild(node);
        EmitContext().AddEmitHelper(visited!, EmitContext().ReadEmitHelpers());
        ExitSubtree(ancestorFacts);
        return visited;
    }

    /// <summary>Go: <c>func (tx *forawaitTransformer) visitForOfStatement</c>（forawait.go:333-344）。visits a ForOfStatement and converts it into a ES2015-compatible ForOfStatement.</summary>
    private Node? VisitForOfStatement(ForInOrOfStatement node, LabeledStatement? outermostLabeledStatement)
    {
        var ancestorFacts = EnterSubtree(ForAwaitHierarchyFacts.IterationStatementExcludes, ForAwaitHierarchyFacts.IterationStatementIncludes);
        Node? result;
        if (node.AwaitModifier is not null)
        {
            result = TransformForAwaitOfStatement(node, outermostLabeledStatement, ancestorFacts);
        }
        else
        {
            result = Factory().RestoreEnclosingLabel(Visitor().VisitEachChild(node)!, outermostLabeledStatement);
        }
        ExitSubtree(ancestorFacts);
        return result;
    }

    /// <summary>Go: <c>func (tx *forawaitTransformer) convertForOfStatementHead</c>（forawait.go:346-373）。</summary>
    private Node ConvertForOfStatementHead(ForInOrOfStatement node, Node boundValue, Node nonUserCode)
    {
        var f = Factory();
        var value = f.NewTempVariable();
        EmitContext().AddVariableDeclaration(value);
        var iteratorValueExpression = f.NewAssignmentExpression(value, boundValue);
        var iteratorValueStatement = f.NewExpressionStatement(iteratorValueExpression);
        EmitContext().SetSourceMapRange(iteratorValueStatement, node.Expression!.Loc);

        var exitNonUserCodeExpression = f.NewAssignmentExpression(nonUserCode, f.NewKeywordExpression(Kind.FalseKeyword));
        var exitNonUserCodeStatement = f.NewExpressionStatement(exitNonUserCodeExpression);
        EmitContext().SetSourceMapRange(exitNonUserCodeStatement, node.Expression.Loc);

        var statements = new List<Node> { iteratorValueStatement, exitNonUserCodeStatement };
        var binding = Factory().CreateForOfBindingStatement(node.Initializer!, value);
        statements.Add(Visitor().VisitNode(binding)!);

        var bodyLocation = default(TextRange);
        var statementsLocation = default(TextRange);
        var statement = Visitor().VisitEmbeddedStatement(node.Statement);
        if (IsBlock(statement!))
        {
            statements.AddRange(statement!.StatementList()!.Nodes!);
            bodyLocation = statement.Loc;
            statementsLocation = statement.StatementList()!.Loc;
        }
        else
        {
            statements.Add(statement!);
        }

        var stmtList = f.NewNodeList(statements.ToArray());
        stmtList.Loc = statementsLocation;
        var block = f.NewBlock(stmtList, true);
        block.Loc = bodyLocation;
        return block;
    }

    /// <summary>Go: <c>func (tx *forawaitTransformer) createDownlevelAwait</c>（forawait.go:375-382）。</summary>
    private Node CreateDownlevelAwait(Node expression)
    {
        if ((_enclosingFunctionFlags & FunctionFlags.Generator) != 0)
        {
            return Factory().NewYieldExpression(
                null, /*asteriskToken*/
                Factory().NewAwaitHelper(expression)
            );
        }
        return Factory().NewAwaitExpression(expression);
    }

    /// <summary>Go: <c>func (tx *forawaitTransformer) transformForAwaitOfStatement</c>（forawait.go:384-524）。</summary>
    private Node TransformForAwaitOfStatement(ForInOrOfStatement node, LabeledStatement? outermostLabeledStatement, ForAwaitHierarchyFacts ancestorFacts)
    {
        var f = Factory();
        var expression = Visitor().VisitNode(node.Expression);

        Node iterator;
        if (IsIdentifier(expression!))
        {
            iterator = f.NewGeneratedNameForNode(expression);
        }
        else
        {
            iterator = f.NewTempVariable();
        }

        Node result;
        if (IsIdentifier(expression!))
        {
            result = f.NewGeneratedNameForNode(iterator);
        }
        else
        {
            result = f.NewTempVariable();
        }

        var nonUserCode = f.NewTempVariable();
        var done = f.NewTempVariable();
        EmitContext().AddVariableDeclaration(done);
        var errorRecord = f.NewUniqueName("e");
        var catchVariable = f.NewGeneratedNameForNode(errorRecord);
        var returnMethod = f.NewTempVariable();
        var callValues = f.NewAsyncValuesHelper(expression!);
        callValues.Loc = node.Expression!.Loc;
        var callNext = f.NewCallExpression(
            f.NewPropertyAccessExpression(iterator, null, f.NewIdentifier("next"), NodeFlags.None),
            null,
            null,
            f.NewNodeList([]),
            NodeFlags.None
        );
        var getDone = f.NewPropertyAccessExpression(result, null, f.NewIdentifier("done"), NodeFlags.None);
        var getValue = f.NewPropertyAccessExpression(result, null, f.NewIdentifier("value"), NodeFlags.None);
        var callReturn = f.NewFunctionCallCall(returnMethod, iterator, []);

        EmitContext().AddVariableDeclaration(errorRecord);
        EmitContext().AddVariableDeclaration(returnMethod);

        // if we are enclosed in an outer loop ensure we reset 'errorRecord' per each iteration
        Node? initializer;
        if ((ancestorFacts & ForAwaitHierarchyFacts.IterationContainer) != 0)
        {
            initializer = f.InlineExpressions([
                f.NewAssignmentExpression(errorRecord, f.NewVoidZeroExpression()),
                callValues,
            ]);
        }
        else
        {
            initializer = callValues;
        }

        // Build the for statement
        var iteratorDecl = f.NewVariableDeclaration(iterator, null, null, initializer);
        iteratorDecl.Loc = node.Expression.Loc;
        var varDeclList = f.NewVariableDeclarationList(f.NewNodeList([
            f.NewVariableDeclaration(nonUserCode, null, null, f.NewKeywordExpression(Kind.TrueKeyword)),
            iteratorDecl,
            f.NewVariableDeclaration(result, null, null, null),
        ]), NodeFlags.None);
        varDeclList.Loc = node.Expression.Loc;

        var condition = f.InlineExpressions([
            f.NewAssignmentExpression(result, CreateDownlevelAwait(callNext)),
            f.NewAssignmentExpression(done, getDone),
            f.NewPrefixUnaryExpression(Kind.ExclamationToken, done),
        ]);

        var incrementor = f.NewAssignmentExpression(nonUserCode, f.NewKeywordExpression(Kind.TrueKeyword));

        var forStatement = f.NewForStatement(
            varDeclList,
            condition,
            incrementor,
            ConvertForOfStatementHead(node, getValue, nonUserCode)
        );
        forStatement.Loc = node.Loc;
        EmitContext().AddEmitFlags(forStatement, EmitFlags.NoTokenTrailingSourceMaps);
        EmitContext().SetOriginal(forStatement, node);

        // Build the try/catch/finally
        var tryBlock = f.NewBlock(f.NewNodeList([
            f.RestoreEnclosingLabel(forStatement, outermostLabeledStatement),
        ]), true);

        // catch clause: { e_1 = { error: e_2 }; }
        var catchBody = f.NewBlock(f.NewNodeList([
            f.NewExpressionStatement(
                f.NewAssignmentExpression(
                    errorRecord,
                    f.NewObjectLiteralExpression(f.NewNodeList([
                        f.NewPropertyAssignment(null, f.NewIdentifier("error"), null, null, catchVariable),
                    ]), false)
                )
            ),
        ]), false);
        EmitContext().AddEmitFlags(catchBody, EmitFlags.SingleLine);
        var catchClause = f.NewCatchClause(
            f.NewVariableDeclaration(catchVariable, null, null, null),
            catchBody
        );

        // finally block
        // inner try: if (!nonUserCode && !done && (returnMethod = iterator.return)) await returnMethod.call(iterator);
        var innerIfCondition = f.NewBinaryExpression(
            null,
            f.NewBinaryExpression(
                null,
                f.NewPrefixUnaryExpression(Kind.ExclamationToken, nonUserCode),
                null,
                f.NewToken(Kind.AmpersandAmpersandToken),
                f.NewPrefixUnaryExpression(Kind.ExclamationToken, done)
            ),
            null,
            f.NewToken(Kind.AmpersandAmpersandToken),
            f.NewAssignmentExpression(
                returnMethod,
                f.NewPropertyAccessExpression(iterator, null, f.NewIdentifier("return"), NodeFlags.None)
            )
        );
        var innerIfStatement = f.NewIfStatement(
            innerIfCondition,
            f.NewExpressionStatement(CreateDownlevelAwait(callReturn)),
            null
        );
        EmitContext().AddEmitFlags(innerIfStatement, EmitFlags.SingleLine);

        var innerTryBlock = f.NewBlock(f.NewNodeList([innerIfStatement]), false);

        // inner finally: if (errorRecord) throw errorRecord.error;
        var innerFinallyIf = f.NewIfStatement(
            errorRecord,
            f.NewThrowStatement(
                f.NewPropertyAccessExpression(errorRecord, null, f.NewIdentifier("error"), NodeFlags.None)
            ),
            null
        );
        EmitContext().AddEmitFlags(innerFinallyIf, EmitFlags.SingleLine);
        var innerFinallyBlock = f.NewBlock(f.NewNodeList([innerFinallyIf]), false);
        EmitContext().AddEmitFlags(innerFinallyBlock, EmitFlags.SingleLine);

        var innerTryStatement = f.NewTryStatement(innerTryBlock, null, innerFinallyBlock);
        var finallyBlock = f.NewBlock(f.NewNodeList([innerTryStatement]), true);

        return f.NewTryStatement(tryBlock, catchClause, finallyBlock);
    }

    /// <summary>Go: <c>func (tx *forawaitTransformer) visitConstructorDeclaration</c>（forawait.go:526-541）。</summary>
    private Node? VisitConstructorDeclaration(Node node)
    {
        var decl = node.AsConstructorDeclaration()!;
        var savedEnclosingFunctionFlags = _enclosingFunctionFlags;
        _enclosingFunctionFlags = GetFunctionFlags(node);
        var updated = Factory().UpdateConstructorDeclaration(
            decl,
            decl.Modifiers(),
            null, /*typeParameters*/
            EmitContext().VisitParameters(decl.Parameters!, Visitor()),
            null, /*returnType*/
            null, /*fullSignature*/
            EmitContext().VisitFunctionBody(node.Body()!, Visitor())
        );
        _enclosingFunctionFlags = savedEnclosingFunctionFlags;
        return updated;
    }

    /// <summary>Go: <c>func (tx *forawaitTransformer) visitGetAccessorDeclaration</c>（forawait.go:543-559）。</summary>
    private Node? VisitGetAccessorDeclaration(Node node)
    {
        var decl = node.AsGetAccessorDeclaration()!;
        var savedEnclosingFunctionFlags = _enclosingFunctionFlags;
        _enclosingFunctionFlags = GetFunctionFlags(node);
        var updated = Factory().UpdateGetAccessorDeclaration(
            decl,
            decl.Modifiers(),
            Visitor().VisitNode(decl.Name()),
            null, /*typeParameters*/
            EmitContext().VisitParameters(decl.Parameters!, Visitor()),
            null, /*returnType*/
            null, /*fullSignature*/
            EmitContext().VisitFunctionBody(node.Body()!, Visitor())
        );
        _enclosingFunctionFlags = savedEnclosingFunctionFlags;
        return updated;
    }

    /// <summary>Go: <c>func (tx *forawaitTransformer) visitSetAccessorDeclaration</c>（forawait.go:561-577）。</summary>
    private Node? VisitSetAccessorDeclaration(Node node)
    {
        var decl = node.AsSetAccessorDeclaration()!;
        var savedEnclosingFunctionFlags = _enclosingFunctionFlags;
        _enclosingFunctionFlags = GetFunctionFlags(node);
        var updated = Factory().UpdateSetAccessorDeclaration(
            decl,
            decl.Modifiers(),
            Visitor().VisitNode(decl.Name()),
            null, /*typeParameters*/
            EmitContext().VisitParameters(decl.Parameters!, Visitor()),
            null, /*returnType*/
            null, /*fullSignature*/
            EmitContext().VisitFunctionBody(node.Body()!, Visitor())
        );
        _enclosingFunctionFlags = savedEnclosingFunctionFlags;
        return updated;
    }

    /// <summary>Go: <c>func (tx *forawaitTransformer) visitMethodDeclaration</c>（forawait.go:579-619）。</summary>
    private Node? VisitMethodDeclaration(Node node)
    {
        var decl = node.AsMethodDeclaration()!;
        var savedEnclosingFunctionFlags = _enclosingFunctionFlags;
        _enclosingFunctionFlags = GetFunctionFlags(node);

        ModifierList? modifiers;
        if ((_enclosingFunctionFlags & FunctionFlags.Generator) != 0)
        {
            modifiers = VisitModifiersNoAsync(decl.Modifiers());
        }
        else
        {
            modifiers = decl.Modifiers();
        }

        Node? asteriskToken;
        if ((_enclosingFunctionFlags & FunctionFlags.Async) != 0)
        {
            asteriskToken = null;
        }
        else
        {
            asteriskToken = decl.AsteriskToken;
        }

        NodeList? parameters;
        Node? body;
        if ((_enclosingFunctionFlags & FunctionFlags.Async) != 0 && (_enclosingFunctionFlags & FunctionFlags.Generator) != 0)
        {
            parameters = TransformAsyncGeneratorFunctionParameterList(node);
            body = TransformAsyncGeneratorFunctionBody(node);
        }
        else
        {
            parameters = EmitContext().VisitParameters(decl.Parameters!, Visitor());
            body = EmitContext().VisitFunctionBody(node.Body()!, Visitor());
        }

        var updated = Factory().UpdateMethodDeclaration(
            decl,
            modifiers,
            asteriskToken,
            Visitor().VisitNode(decl.Name()),
            null, /*postfixToken*/
            null, /*typeParameters*/
            parameters,
            null, /*returnType*/
            null, /*fullSignature*/
            body
        );
        _enclosingFunctionFlags = savedEnclosingFunctionFlags;
        return updated;
    }

    /// <summary>Go: <c>func (tx *forawaitTransformer) visitFunctionDeclaration</c>（forawait.go:621-667）。</summary>
    private Node? VisitFunctionDeclaration(Node node)
    {
        var decl = node.AsFunctionDeclaration()!;
        var savedEnclosingFunctionFlags = _enclosingFunctionFlags;
        _enclosingFunctionFlags = GetFunctionFlags(node);

        ModifierList? modifiers;
        if ((_enclosingFunctionFlags & FunctionFlags.Generator) != 0)
        {
            modifiers = VisitModifiersNoAsync(decl.Modifiers());
        }
        else
        {
            modifiers = decl.Modifiers();
        }

        Node? asteriskToken;
        if ((_enclosingFunctionFlags & FunctionFlags.Async) != 0)
        {
            asteriskToken = null;
        }
        else
        {
            asteriskToken = decl.AsteriskToken;
        }

        NodeList? parameters;
        Node? body;
        if ((_enclosingFunctionFlags & FunctionFlags.Async) != 0 && (_enclosingFunctionFlags & FunctionFlags.Generator) != 0)
        {
            parameters = TransformAsyncGeneratorFunctionParameterList(node);
            body = TransformAsyncGeneratorFunctionBody(node);
        }
        else
        {
            parameters = EmitContext().VisitParameters(decl.Parameters!, Visitor());
            body = EmitContext().VisitFunctionBody(node.Body()!, Visitor());
        }

        var updated = Factory().UpdateFunctionDeclaration(
            decl,
            modifiers,
            asteriskToken,
            decl.Name(),
            null, /*typeParameters*/
            parameters,
            null, /*returnType*/
            null, /*fullSignature*/
            body
        );
        _enclosingFunctionFlags = savedEnclosingFunctionFlags;
        return updated;
    }

    /// <summary>Go: <c>func (tx *forawaitTransformer) visitArrowFunction</c>（forawait.go:669-687）。</summary>
    private Node? VisitArrowFunction(Node node)
    {
        var decl = node.AsArrowFunction()!;
        var savedEnclosingFunctionFlags = _enclosingFunctionFlags;
        _enclosingFunctionFlags = GetFunctionFlags(node);
        var updated = Factory().UpdateArrowFunction(
            decl,
            decl.Modifiers(),
            null, /*typeParameters*/
            EmitContext().VisitParameters(decl.Parameters!, Visitor()),
            null, /*returnType*/
            null, /*fullSignature*/
            decl.EqualsGreaterThanToken,
            EmitContext().VisitFunctionBody(node.Body()!, Visitor())
        );
        _enclosingFunctionFlags = savedEnclosingFunctionFlags;
        return updated;
    }

    /// <summary>Go: <c>func (tx *forawaitTransformer) visitFunctionExpression</c>（forawait.go:689-735）。</summary>
    private Node? VisitFunctionExpression(Node node)
    {
        var decl = node.AsFunctionExpression()!;
        var savedEnclosingFunctionFlags = _enclosingFunctionFlags;
        _enclosingFunctionFlags = GetFunctionFlags(node);

        ModifierList? modifiers;
        if ((_enclosingFunctionFlags & FunctionFlags.Generator) != 0)
        {
            modifiers = VisitModifiersNoAsync(decl.Modifiers());
        }
        else
        {
            modifiers = decl.Modifiers();
        }

        Node? asteriskToken;
        if ((_enclosingFunctionFlags & FunctionFlags.Async) != 0)
        {
            asteriskToken = null;
        }
        else
        {
            asteriskToken = decl.AsteriskToken;
        }

        NodeList? parameters;
        Node? body;
        if ((_enclosingFunctionFlags & FunctionFlags.Async) != 0 && (_enclosingFunctionFlags & FunctionFlags.Generator) != 0)
        {
            parameters = TransformAsyncGeneratorFunctionParameterList(node);
            body = TransformAsyncGeneratorFunctionBody(node);
        }
        else
        {
            parameters = EmitContext().VisitParameters(decl.Parameters!, Visitor());
            body = EmitContext().VisitFunctionBody(node.Body()!, Visitor());
        }

        var updated = Factory().UpdateFunctionExpression(
            decl,
            modifiers,
            asteriskToken,
            decl.Name(),
            null, /*typeParameters*/
            parameters,
            null, /*returnType*/
            null, /*fullSignature*/
            body
        );
        _enclosingFunctionFlags = savedEnclosingFunctionFlags;
        return updated;
    }

    /// <summary>Go: <c>func (tx *forawaitTransformer) transformAsyncGeneratorFunctionParameterList</c>（forawait.go:737-762）。</summary>
    private NodeList TransformAsyncGeneratorFunctionParameterList(Node node)
    {
        if (IsSimpleParameterList(Parameters(node)))
        {
            return EmitContext().VisitParameters(node.ParameterList()!, Visitor())!;
        }
        // Add fixed parameters to preserve the function's `length` property.
        var newParameters = new List<Node>();
        foreach (var parameter in Parameters(node))
        {
            var param = parameter.AsParameterDeclaration()!;
            if (param.Initializer is not null || param.DotDotDotToken is not null)
            {
                break;
            }
            var newParameter = Factory().NewParameterDeclaration(
                null,
                null,
                Factory().NewGeneratedNameForNodeEx(param.Name(), new AutoGenerateOptions { Flags = GeneratedIdentifierFlags.ReservedInNestedScopes }),
                null,
                null,
                null
            );
            newParameters.Add(newParameter);
        }
        var newParametersArray = Factory().NewNodeList(newParameters.ToArray());
        newParametersArray.Loc = node.ParameterList()!.Loc;
        return newParametersArray;
    }

    /// <summary>Go: <c>func (tx *forawaitTransformer) transformAsyncGeneratorFunctionBody</c>（forawait.go:764-857）。</summary>
    private Node TransformAsyncGeneratorFunctionBody(Node node)
    {
        var f = Factory();
        NodeList? innerParameters = null;
        if (!IsSimpleParameterList(Parameters(node)))
        {
            innerParameters = EmitContext().VisitParameters(node.ParameterList()!, Visitor());
        }

        var savedCapturedSuperProperties = _super._capturedSuperProperties;
        var savedHasSuperElementAccess = _super._hasSuperElementAccess;
        var savedHasSuperPropertyAssignment = _super._hasSuperPropertyAssignment;
        var savedSuperBinding = _super._superBinding;
        var savedSuperIndexBinding = _super._superIndexBinding;
        _super._capturedSuperProperties = new OrderedSet<string>();
        _super._hasSuperElementAccess = false;
        _super._hasSuperPropertyAssignment = false;
        _super._superBinding = (Identifier)f.NewUniqueNameEx("_super", new AutoGenerateOptions { Flags = GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.FileLevel });
        _super._superIndexBinding = (Identifier)f.NewUniqueNameEx("_superIndex", new AutoGenerateOptions { Flags = GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.FileLevel });

        var asyncBody = f.UpdateBlock(
            node.Body()!.AsBlock()!,
            Visitor().VisitNodes(node.Body()!.StatementList()),
            node.Body()!.AsBlock()!.MultiLine
        );
        asyncBody = f.UpdateBlock(
            asyncBody.AsBlock()!,
            EmitContext().EndAndMergeVariableEnvironmentList(asyncBody.StatementList()),
            asyncBody.AsBlock()!.MultiLine
        );

        // Substitute super property accesses with _super/_superIndex helpers
        var emitSuperHelpers = _super._capturedSuperProperties.Size() > 0 || _super._hasSuperElementAccess;
        if (emitSuperHelpers)
        {
            asyncBody = _super.SubstituteSuperAccessesInBody(asyncBody)!;
        }

        NodeList innerParams;
        if (innerParameters is not null)
        {
            innerParams = innerParameters;
        }
        else
        {
            innerParams = f.NewNodeList([]);
        }

        Node? name = null;
        if (node.Name() is not null)
        {
            name = f.NewGeneratedNameForNode(node.Name());
        }

        var generatorFunc = f.NewFunctionExpression(
            null, /*modifiers*/
            f.NewToken(Kind.AsteriskToken),
            name,
            null, /*typeParameters*/
            innerParams,
            null, /*returnType*/
            null, /*fullSignature*/
            asyncBody
        );

        var returnStatement = f.NewReturnStatement(
            f.NewAsyncGeneratorHelper(
                generatorFunc,
                (_forAwaitHierarchyFacts & ForAwaitHierarchyFacts.HasLexicalThis) != 0
            )
        );

        EmitContext().StartVariableEnvironment();
        if (emitSuperHelpers)
        {
            if (_super._capturedSuperProperties.Size() > 0)
            {
                EmitContext().AddInitializationStatement(_super.CreateSuperAccessVariableStatement()!);
            }
        }

        var outerStatements = new Node[] { returnStatement };

        var block = f.UpdateBlock(
            node.Body()!.AsBlock()!,
            EmitContext().EndAndMergeVariableEnvironmentList(f.NewNodeList(outerStatements)),
            node.Body()!.AsBlock()!.MultiLine
        );

        if (emitSuperHelpers && _super._hasSuperElementAccess)
        {
            if (_super._hasSuperPropertyAssignment)
            {
                EmitContext().AddEmitHelper(block, AdvancedAsyncSuperHelper);
            }
            else
            {
                EmitContext().AddEmitHelper(block, AsyncSuperHelper);
            }
        }

        _super._capturedSuperProperties = savedCapturedSuperProperties;
        _super._hasSuperElementAccess = savedHasSuperElementAccess;
        _super._hasSuperPropertyAssignment = savedHasSuperPropertyAssignment;
        _super._superBinding = savedSuperBinding;
        _super._superIndexBinding = savedSuperIndexBinding;

        return block;
    }
}

/// <summary>Go: 包级函数/类型构造（`estransforms` 包）。</summary>
public static partial class EsTransforms
{
    /// <summary>Go: <c>func newforawaitTransformer(opts *transformers.TransformOptions) *transformers.Transformer</c>（forawait.go:56-67）。</summary>
    internal static Transformer? NewForAwaitTransformer(TransformOptions opts)
    {
        var tx = new ForAwaitTransformer
        {
            _compilerOptions = opts.CompilerOptions,
        };
        var result = tx.NewTransformer(tx.Visit, opts.Context);
        tx._super.InitSuperAccessVisitor(tx.EmitContext(), tx.Factory());
        tx._fallbackNodeVisitor = tx.EmitContext().NewNodeVisitor(tx.VisitFallback);
        tx._noAsyncModifierVisitor = tx.EmitContext().NewNodeVisitor(static node => node.Kind == Kind.AsyncKeyword ? null : node);
        return result;
    }

    /// <summary>Go: <c>func unwrapInnermostStatementOfLabel(node *ast.LabeledStatement) *ast.Node</c>（forawait.go:308-317）。follows LabeledStatement chains to find the innermost statement.</summary>
    internal static Node UnwrapInnermostStatementOfLabel(LabeledStatement node)
    {
        while (true)
        {
            if (node.Statement!.Kind != Kind.LabeledStatement)
            {
                return node.Statement;
            }
            node = node.Statement.AsLabeledStatement()!;
        }
    }
}
