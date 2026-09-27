// ported from: internal/transformers/estransforms/objectrestspread.go（593 行，全部：objectRestSpreadTransformer +
//               newObjectRestSpreadTransformer、visit、visitSourceFile、visitParameter、
//               collectParametersWithPrecedingObjectRestOrSpread、oldParamScope/enterParameterListContext/
//               exitParameterListContext、visitContructorDeclaration、visitGetAccessorDeclaration、
//               visitSetAccessorDeclaration、visitMethodDeclaration、visitFunctionDeclaration、visitArrowFunction、
//               visitFunctionExpression、transformFunctionBody、collectObjectRestAssignments、visitCatchClause、
//               visitVariableStatement、visitVariableDeclaration、visitVariableDeclarationWorker、
//               visitForOftatement、visitBinaryExpression、visitObjectLiteralExpression、chunkObjectLiteralElements）
//
// 与 Go 的差异（逐条）：
// 1) Go 的 `type objectRestSpreadTransformer struct { transformers.Transformer; … }`（嵌入）→ C# 的
//    `internal sealed class ObjectRestSpreadTransformer : Transformer`（继承，见 Transformers/Transformer.cs 契约）；
//    `compilerOptions` 是构造期赋值的只读字段 → `internal` 构造函数承接 Go 的结构体字面量。
// 2) Go 的 `defer func() { ch.expressionResultIsUnused = … }()`（visit 里）→ `try/finally`（Go 的 defer 语义；
//    `visitVariableStatement`/`visitVariableDeclaration` 里的手工保存/恢复**不是** defer，照 Go 原样写）。
// 3) Go 的 `map[*ast.Node]struct{}`（只判存在/插入）→ C# `HashSet<Node>`（仓库先例：`Checker.C4.AmbientModules.cs` 的
//    "Go 的 map-set → C# 集合"；Node 未重载 Equals/GetHashCode → 引用相等，与 Go 的指针键一致）。
//    Go 的命名类型 `type oldParamScope map[*ast.Node]struct{}` 只是类型转换用的别名（`oldParamScope(old)` 无运行时效果）
//    → C# 直接用 `HashSet<Node>?`，并保留 `Enter/ExitParameterListContext` 两个方法名。
// 4) Go 的 `append`（`allArgs`/`objects`/`statements`/`prefix`/`suffix`）→ `List<Node>` + `ToArray()`（§3.2）；
//    `suffix = body.Statements()[i:]` → `statements[i..].ToList()`（Go 的切片视图只被顺序遍历）。
// 5) Go 的 `chunkObject = nil`（flush 后换一个新的切片头）→ `chunkObject = []`（**不能**用 Clear，见
//    EsDecoratorTransformer 文件头第 4 条的同类语义）。
// 6) `core.TextRange` 零值 → C# `default(TextRange)`（`TextRange` 是 readonly struct，Pos/End 都是 0）。
// 7) Go 的 `declarations.AsSyntaxList().Children` 是 `[]*ast.Node`（SyntaxList 的字段）→ C# 的
//    `AsSyntaxList()!.Children ?? []`（Go 的 nil 切片长度 0 → C# 不能解引用 null）。
// 8) Go 的 `name.Initializer()`/`node.Name()`/`parameter.Name()` 是 `nodeData` 方法 → C# 保持方法调用
//    （生成类里的同名字段是 `_name`，见 docs/ast.md §2）。
// 9) Go 保留的拼写（`visitForOftatement`、`visitContructorDeclaration`）在 C# 里同样保留。
// 10) Go 的 `transformers.FlattenDestructuringBinding/FlattenDestructuringAssignment/IsSimpleCopiableExpression`
//     → `using static TypeScript.Transformers.Transformers;` 后裸名调用（T1 落地的 `Transformers` 静态类）。
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Printer;
using static TypeScript.Ast.Ast;
using static TypeScript.Transformers.EsTransforms.EsTransforms;
using static TypeScript.Transformers.Transformers;

namespace TypeScript.Transformers.EsTransforms;

internal sealed class ObjectRestSpreadTransformer : Transformer
{
    private readonly CompilerOptions _compilerOptions;

    private bool _inExportedVariableStatement;
    private bool _expressionResultIsUnused;

    private HashSet<Node>? _parametersWithPrecedingObjectRestOrSpread;

    internal ObjectRestSpreadTransformer(CompilerOptions compilerOptions)
    {
        _compilerOptions = compilerOptions;
    }

    /// <summary>Go: <c>func (ch *objectRestSpreadTransformer) visit(node *ast.Node) *ast.Node</c>。</summary>
    internal Node? Visit(Node node)
    {
        if ((node.SubtreeFacts & SubtreeFacts.ContainsESObjectRestOrSpread) == 0 && _parametersWithPrecedingObjectRestOrSpread is null)
        {
            return node;
        }

        // Save the expressionResultIsUnused flag set by the parent for this node,
        // then reset to false for children (the default). Specific cases below override as needed.
        var expressionResultIsUnused = _expressionResultIsUnused;
        _expressionResultIsUnused = false;
        try
        {
            switch (node.Kind)
            {
                case Kind.SourceFile:
                    return VisitSourceFile(node.AsSourceFile()!);
                case Kind.ObjectLiteralExpression:
                    return VisitObjectLiteralExpression(node.AsObjectLiteralExpression()!);
                case Kind.BinaryExpression:
                    return VisitBinaryExpression(node.AsBinaryExpression()!, expressionResultIsUnused);
                case Kind.ExpressionStatement:
                    _expressionResultIsUnused = true;
                    return Visitor().VisitEachChild(node);
                case Kind.ParenthesizedExpression:
                    _expressionResultIsUnused = expressionResultIsUnused;
                    return Visitor().VisitEachChild(node);
                case Kind.ForOfStatement:
                    return VisitForOftatement(node.AsForInOrOfStatement()!);
                case Kind.VariableStatement:
                    return VisitVariableStatement(node.AsVariableStatement()!);
                case Kind.VariableDeclaration:
                    return VisitVariableDeclaration(node.AsVariableDeclaration()!);
                case Kind.CatchClause:
                    return VisitCatchClause(node.AsCatchClause()!);
                case Kind.Parameter:
                    return VisitParameter(node.AsParameterDeclaration()!);
                case Kind.Constructor:
                    return VisitContructorDeclaration(node.AsConstructorDeclaration()!);
                case Kind.GetAccessor:
                    return VisitGetAccessorDeclaration(node.AsGetAccessorDeclaration()!);
                case Kind.SetAccessor:
                    return VisitSetAccessorDeclaration(node.AsSetAccessorDeclaration()!);
                case Kind.MethodDeclaration:
                    return VisitMethodDeclaration(node.AsMethodDeclaration()!);
                case Kind.FunctionDeclaration:
                    return VisitFunctionDeclaration(node.AsFunctionDeclaration()!);
                case Kind.ArrowFunction:
                    return VisitArrowFunction(node.AsArrowFunction()!);
                case Kind.FunctionExpression:
                    return VisitFunctionExpression(node.AsFunctionExpression()!);
                default:
                    return Visitor().VisitEachChild(node);
            }
        }
        finally
        {
            _expressionResultIsUnused = expressionResultIsUnused;
        }
    }

    /// <summary>Go: <c>func (ch *objectRestSpreadTransformer) visitSourceFile(node *ast.SourceFile) *ast.Node</c>。</summary>
    private Node? VisitSourceFile(SourceFile node)
    {
        var visited = Visitor().VisitEachChild(node);
        EmitContext().AddEmitHelper(visited!, EmitContext().ReadEmitHelpers());
        return visited;
    }

    /// <summary>Go: <c>func (ch *objectRestSpreadTransformer) visitParameter(node *ast.ParameterDeclaration) *ast.Node</c>。</summary>
    private Node? VisitParameter(ParameterDeclaration node)
    {
        if (_parametersWithPrecedingObjectRestOrSpread is not null)
        {
            if (_parametersWithPrecedingObjectRestOrSpread.Contains(node))
            {
                var name = node.Name();
                if (IsBindingPattern(name))
                {
                    name = Factory().NewGeneratedNameForNode(node);
                }

                return Factory().UpdateParameterDeclaration(
                    node,
                    null,
                    node.DotDotDotToken,
                    name,
                    null,
                    null,
                    null);
            }
        }

        if ((node.SubtreeFacts & SubtreeFacts.ContainsObjectRestOrSpread) != 0)
        {
            // Binding patterns are converted into a generated name and are
            // evaluated inside the function body.
            return Factory().UpdateParameterDeclaration(
                node,
                null,
                node.DotDotDotToken,
                Factory().NewGeneratedNameForNode(node),
                null,
                null,
                Visitor().VisitNode(node.Initializer));
        }

        return Visitor().VisitEachChild(node);
    }

    /// <summary>Go: <c>func (ch *objectRestSpreadTransformer) collectParametersWithPrecedingObjectRestOrSpread(node *ast.Node) map[*ast.Node]struct{}</c>。</summary>
    private static HashSet<Node>? CollectParametersWithPrecedingObjectRestOrSpread(Node node)
    {
        HashSet<Node>? result = null;
        foreach (var parameter in node.Parameters() ?? [])
        {
            if (result is not null)
            {
                result.Add(parameter);
            }
            else if ((parameter.SubtreeFacts & SubtreeFacts.ContainsObjectRestOrSpread) != 0)
            {
                result = [];
            }
        }

        return result;
    }

    /// <summary>Go: <c>func (ch *objectRestSpreadTransformer) enterParameterListContext(node *ast.Node) oldParamScope</c>。</summary>
    private HashSet<Node>? EnterParameterListContext(Node node)
    {
        var old = _parametersWithPrecedingObjectRestOrSpread;
        _parametersWithPrecedingObjectRestOrSpread = CollectParametersWithPrecedingObjectRestOrSpread(node);
        return old;
    }

    /// <summary>Go: <c>func (ch *objectRestSpreadTransformer) exitParameterListContext(scope oldParamScope)</c>。</summary>
    private void ExitParameterListContext(HashSet<Node>? scope)
    {
        _parametersWithPrecedingObjectRestOrSpread = scope;
    }

    /// <summary>Go: <c>func (ch *objectRestSpreadTransformer) visitContructorDeclaration(node *ast.ConstructorDeclaration) *ast.Node</c>。</summary>
    private Node? VisitContructorDeclaration(ConstructorDeclaration node)
    {
        var old = EnterParameterListContext(node);
        try
        {
            return Factory().UpdateConstructorDeclaration(
                node,
                node.Modifiers(),
                null,
                Visitor().VisitNodes(node.Parameters),
                null,
                null,
                TransformFunctionBody(node));
        }
        finally
        {
            ExitParameterListContext(old);
        }
    }

    /// <summary>Go: <c>func (ch *objectRestSpreadTransformer) visitGetAccessorDeclaration(node *ast.GetAccessorDeclaration) *ast.Node</c>。</summary>
    private Node? VisitGetAccessorDeclaration(GetAccessorDeclaration node)
    {
        var old = EnterParameterListContext(node);
        try
        {
            return Factory().UpdateGetAccessorDeclaration(
                node,
                node.Modifiers(),
                Visitor().VisitNode(node.Name()),
                null,
                Visitor().VisitNodes(node.Parameters),
                null,
                null,
                TransformFunctionBody(node));
        }
        finally
        {
            ExitParameterListContext(old);
        }
    }

    /// <summary>Go: <c>func (ch *objectRestSpreadTransformer) visitSetAccessorDeclaration(node *ast.SetAccessorDeclaration) *ast.Node</c>。</summary>
    private Node? VisitSetAccessorDeclaration(SetAccessorDeclaration node)
    {
        var old = EnterParameterListContext(node);
        try
        {
            return Factory().UpdateSetAccessorDeclaration(
                node,
                node.Modifiers(),
                Visitor().VisitNode(node.Name()),
                null,
                Visitor().VisitNodes(node.Parameters),
                null,
                null,
                TransformFunctionBody(node));
        }
        finally
        {
            ExitParameterListContext(old);
        }
    }

    /// <summary>Go: <c>func (ch *objectRestSpreadTransformer) visitMethodDeclaration(node *ast.MethodDeclaration) *ast.Node</c>。</summary>
    private Node? VisitMethodDeclaration(MethodDeclaration node)
    {
        var old = EnterParameterListContext(node);
        try
        {
            return Factory().UpdateMethodDeclaration(
                node,
                node.Modifiers(),
                node.AsteriskToken,
                Visitor().VisitNode(node.Name()),
                node.PostfixToken,
                null,
                Visitor().VisitNodes(node.Parameters),
                null,
                null,
                TransformFunctionBody(node));
        }
        finally
        {
            ExitParameterListContext(old);
        }
    }

    /// <summary>Go: <c>func (ch *objectRestSpreadTransformer) visitFunctionDeclaration(node *ast.FunctionDeclaration) *ast.Node</c>。</summary>
    private Node? VisitFunctionDeclaration(FunctionDeclaration node)
    {
        var old = EnterParameterListContext(node);
        try
        {
            return Factory().UpdateFunctionDeclaration(
                node,
                node.Modifiers(),
                node.AsteriskToken,
                Visitor().VisitNode(node.Name()),
                null,
                Visitor().VisitNodes(node.Parameters),
                null,
                null,
                TransformFunctionBody(node));
        }
        finally
        {
            ExitParameterListContext(old);
        }
    }

    /// <summary>Go: <c>func (ch *objectRestSpreadTransformer) visitArrowFunction(node *ast.ArrowFunction) *ast.Node</c>。</summary>
    private Node? VisitArrowFunction(ArrowFunction node)
    {
        var old = EnterParameterListContext(node);
        try
        {
            return Factory().UpdateArrowFunction(
                node,
                node.Modifiers(),
                null,
                Visitor().VisitNodes(node.Parameters),
                null,
                null,
                node.EqualsGreaterThanToken,
                TransformFunctionBody(node));
        }
        finally
        {
            ExitParameterListContext(old);
        }
    }

    /// <summary>Go: <c>func (ch *objectRestSpreadTransformer) visitFunctionExpression(node *ast.FunctionExpression) *ast.Node</c>。</summary>
    private Node? VisitFunctionExpression(FunctionExpression node)
    {
        var old = EnterParameterListContext(node);
        try
        {
            return Factory().UpdateFunctionExpression(
                node,
                node.Modifiers(),
                node.AsteriskToken,
                Visitor().VisitNode(node.Name()),
                null,
                Visitor().VisitNodes(node.Parameters),
                null,
                null,
                TransformFunctionBody(node));
        }
        finally
        {
            ExitParameterListContext(old);
        }
    }

    /// <summary>Go: <c>func (ch *objectRestSpreadTransformer) transformFunctionBody(node *ast.Node) *ast.Node</c>。</summary>
    private Node? TransformFunctionBody(Node node)
    {
        // EmitContext().VisitFunctionBody is not used here because this transformer needs to inject
        // object rest assignments between visiting the body and merging the variable environment.
        EmitContext().StartVariableEnvironment();
        var body = Visitor().VisitNode(node.Body());
        var extras = EmitContext().EndVariableEnvironment();
        EmitContext().StartVariableEnvironment();
        var newStatements = CollectObjectRestAssignments(node);
        extras = EmitContext().EndAndMergeVariableEnvironment(extras);
        if (newStatements.Length == 0 && (extras?.Length ?? 0) == 0)
        {
            return body;
        }

        if (body is null)
        {
            body = Factory().NewBlock(Factory().NewNodeList([]), true);
        }

        var prefix = new List<Node>();
        var suffix = new List<Node>();
        if (IsBlock(body))
        {
            var custom = false;
            var statements = body.Statements() ?? [];
            for (var i = 0; i < statements.Length; i++)
            {
                var statement = statements[i];
                if (!custom && IsPrologueDirective(statement))
                {
                    prefix.Add(statement);
                }
                else if ((EmitContext().EmitFlags(statement) & EmitFlags.CustomPrologue) != 0)
                {
                    custom = true;
                    prefix.Add(statement);
                }
                else
                {
                    suffix = statements[i..].ToList();
                    break;
                }
            }
        }
        else
        {
            var ret = Factory().NewReturnStatement(body);
            ret.Loc = body.Loc;
            var list = Factory().NewNodeList([]);
            list.Loc = body.Loc;
            body = Factory().NewBlock(list, true);
            suffix.Add(ret);
        }

        var merged = new List<Node>();
        merged.AddRange(prefix);
        if (extras is not null)
        {
            merged.AddRange(extras);
        }

        merged.AddRange(newStatements);
        merged.AddRange(suffix);
        var newStatementList = Factory().NewNodeList(merged.ToArray());
        newStatementList.Loc = body.StatementList()!.Loc;
        return Factory().UpdateBlock(body.AsBlock()!, newStatementList, body.AsBlock()!.MultiLine);
    }

    /// <summary>Go: <c>func (ch *objectRestSpreadTransformer) collectObjectRestAssignments(node *ast.Node) []*ast.Node</c>。</summary>
    private Node[] CollectObjectRestAssignments(Node node)
    {
        var containsPrecedingObjectRestOrSpread = false;
        var results = new List<Node>();
        foreach (var parameter in node.Parameters() ?? [])
        {
            if (containsPrecedingObjectRestOrSpread)
            {
                if (IsBindingPattern(parameter.Name()))
                {
                    // In cases where a binding pattern is simply '[]' or '{}',
                    // we usually don't want to emit a var declaration; however, in the presence
                    // of an initializer, we must emit that expression to preserve side effects.
                    if ((parameter.Name()!.Elements()?.Length ?? 0) > 0)
                    {
                        var declarations = FlattenDestructuringBinding(
                            this,
                            parameter,
                            Factory().NewGeneratedNameForNode(parameter),
                            FlattenLevel.All,
                            false,
                            false);
                        if (declarations is not null)
                        {
                            var declarationList = Factory().NewVariableDeclarationList(Factory().NewNodeList([]), NodeFlags.None);
                            var decls = new Node[] { declarations };
                            if (declarations.Kind == Kind.SyntaxList)
                            {
                                decls = declarations.AsSyntaxList()!.Children ?? [];
                            }

                            var declarationListNode = declarationList.AsVariableDeclarationList()!;
                            declarationListNode.Declarations!.Nodes = [.. declarationListNode.Declarations!.Nodes ?? [], .. decls];
                            var statement = Factory().NewVariableStatement(null, declarationList);
                            EmitContext().AddEmitFlags(statement, EmitFlags.CustomPrologue);
                            results.Add(statement);
                        }
                    }
                    else if (parameter.Initializer() is not null)
                    {
                        var name = Factory().NewGeneratedNameForNode(parameter);
                        var initializer = Visitor().VisitNode(parameter.Initializer());
                        var assignment = Factory().NewAssignmentExpression(name, initializer!);
                        var statement = Factory().NewExpressionStatement(assignment);
                        EmitContext().AddEmitFlags(statement, EmitFlags.CustomPrologue);
                        results.Add(statement);
                    }
                }
                else if (parameter.Initializer() is not null)
                {
                    // Converts a parameter initializer into a function body statement, i.e.:
                    //
                    //  function f(x = 1) { }
                    //
                    // becomes
                    //
                    //  function f(x) {
                    //    if (typeof x === "undefined") { x = 1; }
                    //  }
                    var name = parameter.Name()!.Clone(Factory())!;
                    name.Loc = parameter.Name()!.Loc;
                    EmitContext().AddEmitFlags(name, EmitFlags.NoSourceMap);

                    var initializer = Visitor().VisitNode(parameter.Initializer());
                    EmitContext().AddEmitFlags(initializer!, EmitFlags.NoSourceMap | EmitFlags.NoComments);

                    var assignment = Factory().NewAssignmentExpression(name, initializer!);
                    assignment.Loc = parameter.Loc;
                    EmitContext().AddEmitFlags(assignment, EmitFlags.NoComments);

                    var block = Factory().NewBlock(Factory().NewNodeList([Factory().NewExpressionStatement(assignment)]), false);
                    block.Loc = parameter.Loc;
                    EmitContext().AddEmitFlags(block, EmitFlags.SingleLine | EmitFlags.NoTrailingSourceMap | EmitFlags.NoTokenSourceMaps | EmitFlags.NoComments);

                    var typeCheck = Factory().NewTypeCheck(name.Clone(Factory())!, "undefined");
                    var statement = Factory().NewIfStatement(typeCheck, block, null);
                    statement.Loc = parameter.Loc;
                    EmitContext().AddEmitFlags(statement, EmitFlags.NoTokenSourceMaps | EmitFlags.NoTrailingSourceMap | EmitFlags.CustomPrologue | EmitFlags.NoComments | EmitFlags.StartOnNewLine);
                    results.Add(statement);
                }
            }
            else if ((parameter.SubtreeFacts & SubtreeFacts.ContainsObjectRestOrSpread) != 0)
            {
                containsPrecedingObjectRestOrSpread = true;
                var declarations = FlattenDestructuringBinding(
                    this,
                    parameter,
                    Factory().NewGeneratedNameForNode(parameter),
                    FlattenLevel.ObjectRest,
                    false,
                    true);
                if (declarations is not null)
                {
                    var declarationList = Factory().NewVariableDeclarationList(Factory().NewNodeList([]), NodeFlags.None);
                    var decls = new Node[] { declarations };
                    if (declarations.Kind == Kind.SyntaxList)
                    {
                        decls = declarations.AsSyntaxList()!.Children ?? [];
                    }

                    var declarationListNode = declarationList.AsVariableDeclarationList()!;
                    declarationListNode.Declarations!.Nodes = [.. declarationListNode.Declarations!.Nodes ?? [], .. decls];
                    var statement = Factory().NewVariableStatement(null, declarationList);
                    EmitContext().AddEmitFlags(statement, EmitFlags.CustomPrologue);
                    results.Add(statement);
                }
            }
        }

        return results.ToArray();
    }

    /// <summary>Go: <c>func (ch *objectRestSpreadTransformer) visitCatchClause(node *ast.CatchClause) *ast.Node</c>。</summary>
    private Node? VisitCatchClause(CatchClause node)
    {
        if (node.VariableDeclaration is not null &&
            IsBindingPattern(node.VariableDeclaration.Name()) &&
            (node.VariableDeclaration.Name()!.SubtreeFacts & SubtreeFacts.ContainsObjectRestOrSpread) != 0)
        {
            var name = Factory().NewGeneratedNameForNode(node.VariableDeclaration.Name());
            var updatedDecl = Factory().UpdateVariableDeclaration(node.VariableDeclaration.AsVariableDeclaration()!, node.VariableDeclaration.Name(), null, null, name);
            var visitedBindings = FlattenDestructuringBinding(
                this,
                updatedDecl,
                null,
                FlattenLevel.ObjectRest,
                false,
                false);
            var block = Visitor().VisitNode(node.Block);
            if (visitedBindings is not null)
            {
                Node[] decls;
                if (visitedBindings.Kind == Kind.SyntaxList)
                {
                    decls = visitedBindings.AsSyntaxList()!.Children ?? [];
                }
                else
                {
                    decls = [visitedBindings];
                }

                var newStatement = Factory().NewVariableStatement(null, Factory().NewVariableDeclarationList(Factory().NewNodeList(decls), NodeFlags.None));
                var statements = new List<Node> { newStatement };
                statements.AddRange(block!.Statements() ?? []);
                var statementList = Factory().NewNodeList(statements.ToArray());
                statementList.Loc = block.StatementList()!.Loc;

                block = Factory().UpdateBlock(block.AsBlock()!, statementList, block.AsBlock()!.MultiLine);
            }

            return Factory().UpdateCatchClause(
                node,
                Factory().UpdateVariableDeclaration(node.VariableDeclaration.AsVariableDeclaration()!, name, null, null, null),
                block);
        }

        return Visitor().VisitEachChild(node);
    }

    /// <summary>Go: <c>func (ch *objectRestSpreadTransformer) visitVariableStatement(node *ast.VariableStatement) *ast.Node</c>。</summary>
    private Node? VisitVariableStatement(VariableStatement node)
    {
        if (HasSyntacticModifier(node, ModifierFlags.Export))
        {
            var oldInExportedVariableStatement = _inExportedVariableStatement;
            _inExportedVariableStatement = true;
            var result = Visitor().VisitEachChild(node);
            _inExportedVariableStatement = oldInExportedVariableStatement;
            return result;
        }

        return Visitor().VisitEachChild(node);
    }

    /// <summary>Go: <c>func (ch *objectRestSpreadTransformer) visitVariableDeclaration(node *ast.VariableDeclaration) *ast.Node</c>。</summary>
    private Node? VisitVariableDeclaration(VariableDeclaration node)
    {
        if (_inExportedVariableStatement)
        {
            _inExportedVariableStatement = false;
            var result = VisitVariableDeclarationWorker(node, true);
            _inExportedVariableStatement = true;
            return result;
        }

        return VisitVariableDeclarationWorker(node, false);
    }

    /// <summary>Go: <c>func (ch *objectRestSpreadTransformer) visitVariableDeclarationWorker(node *ast.VariableDeclaration, exported bool) *ast.Node</c>。</summary>
    private Node? VisitVariableDeclarationWorker(VariableDeclaration node, bool exported)
    {
        // If we are here it is because the name contains a binding pattern with a rest somewhere in it.
        if (IsBindingPattern(node.Name()) && (node.SubtreeFacts & SubtreeFacts.ContainsObjectRestOrSpread) != 0)
        {
            return FlattenDestructuringBinding(
                this,
                node,
                null,
                FlattenLevel.ObjectRest,
                exported,
                false);
        }

        return Visitor().VisitEachChild(node);
    }

    /// <summary>Go: <c>func (ch *objectRestSpreadTransformer) visitForOftatement(node *ast.ForInOrOfStatement) *ast.Node</c>（Go 原拼写保留）。</summary>
    private Node? VisitForOftatement(ForInOrOfStatement node)
    {
        if ((node.Initializer!.SubtreeFacts & SubtreeFacts.ContainsObjectRestOrSpread) != 0 ||
            (IsAssignmentPattern(node.Initializer) && ContainsObjectRestOrSpread(node.Initializer!)))
        {
            var initializerWithoutParens = SkipParentheses(node.Initializer)!;
            if (IsVariableDeclarationList(initializerWithoutParens) || IsAssignmentPattern(initializerWithoutParens))
            {
                var bodyLocation = default(TextRange);
                var statementsLocation = default(TextRange);
                var temp = Factory().NewTempVariable();
                var res = Visitor().VisitNode(Factory().CreateForOfBindingStatement(initializerWithoutParens, temp));
                var statements = new List<Node>();
                if (res is not null)
                {
                    statements.Add(res);
                }

                if (IsBlock(node.Statement!))
                {
                    foreach (var statement in node.Statement!.Statements() ?? [])
                    {
                        var visited = Visitor().VisitEachChild(statement);
                        if (visited is not null)
                        {
                            statements.Add(visited);
                        }
                    }

                    bodyLocation = node.Statement.Loc;
                    statementsLocation = node.Statement.StatementList()!.Loc;
                }
                else if (node.Statement is not null)
                {
                    statements.Add(Visitor().VisitEachChild(node.Statement)!);
                    bodyLocation = node.Statement.Loc;
                    statementsLocation = node.Statement.Loc;
                }

                var list = Factory().NewVariableDeclarationList(
                    Factory().NewNodeList([Factory().NewVariableDeclaration(temp, null, null, null)]),
                    NodeFlags.Let);
                list.Loc = node.Initializer.Loc;

                var expr = Visitor().VisitEachChild(node.Expression);

                var statementsList = Factory().NewNodeList(statements.ToArray());
                statementsList.Loc = statementsLocation;

                var block = Factory().NewBlock(statementsList, true);
                block.Loc = bodyLocation;

                return Factory().UpdateForInOrOfStatement(
                    node,
                    node.AwaitModifier,
                    list,
                    expr,
                    block);
            }
        }

        return Visitor().VisitEachChild(node);
    }

    /// <summary>Go: <c>func (ch *objectRestSpreadTransformer) visitBinaryExpression(node *ast.BinaryExpression, expressionResultIsUnused bool) *ast.Node</c>。</summary>
    private Node? VisitBinaryExpression(BinaryExpression node, bool expressionResultIsUnused)
    {
        if (IsDestructuringAssignment(node) && ContainsObjectRestOrSpread(node.Left!))
        {
            return FlattenDestructuringAssignment(
                this,
                node,
                !expressionResultIsUnused,
                FlattenLevel.ObjectRest,
                null);
        }

        if (node.OperatorToken!.Kind == Kind.CommaToken)
        {
            _expressionResultIsUnused = true;
            var left = Visitor().VisitNode(node.Left);
            _expressionResultIsUnused = expressionResultIsUnused;
            var right = Visitor().VisitNode(node.Right);
            return Factory().UpdateBinaryExpression(node, null, left, null, node.OperatorToken, right);
        }

        return Visitor().VisitEachChild(node);
    }

    /// <summary>Go: <c>func (ch *objectRestSpreadTransformer) visitObjectLiteralExpression(node *ast.ObjectLiteralExpression) *ast.Node</c>。</summary>
    private Node? VisitObjectLiteralExpression(ObjectLiteralExpression node)
    {
        if ((node.SubtreeFacts & SubtreeFacts.ContainsObjectRestOrSpread) == 0)
        {
            return Visitor().VisitEachChild(node);
        }

        // spread elements emit like so:
        // non-spread elements are chunked together into object literals, and then all are passed to __assign:
        //     { a, ...o, b } => __assign(__assign({a}, o), {b});
        // If the first element is a spread element, then the first argument to __assign is {}:
        //     { ...o, a, b, ...o2 } => __assign(__assign(__assign({}, o), {a, b}), o2)
        //
        // We cannot call __assign with more than two elements, since any element could cause side effects. For
        // example:
        //      var k = { a: 1, b: 2 };
        //      var o = { a: 3, ...k, b: k.a++ };
        //      // expected: { a: 1, b: 1 }
        // If we translate the above to `__assign({ a: 3 }, k, { b: k.a++ })`, the `k.a++` will evaluate before
        // `k` is spread and we end up with `{ a: 2, b: 1 }`.
        //
        // This also occurs for spread elements, not just property assignments:
        //      var k = { a: 1, get b() { l = { z: 9 }; return 2; } };
        //      var l = { c: 3 };
        //      var o = { ...k, ...l };
        //      // expected: { a: 1, b: 2, z: 9 }
        // If we translate the above to `__assign({}, k, l)`, the `l` will evaluate before `k` is spread and we
        // end up with `{ a: 1, b: 2, c: 3 }`

        var objects = ChunkObjectLiteralElements(node.Properties);
        if (objects.Length > 0 && objects[0].Kind != Kind.ObjectLiteralExpression)
        {
            objects = [Factory().NewObjectLiteralExpression(Factory().NewNodeList(null), false), .. objects];
        }

        var expression = objects[0];
        if (objects.Length > 1)
        {
            for (var i = 0; i < objects.Length; i++)
            {
                var obj = objects[i];
                if (i == 0)
                {
                    continue;
                }

                expression = Factory().NewAssignHelper([expression, obj], _compilerOptions.GetEmitScriptTarget());
            }

            return expression;
        }

        return Factory().NewAssignHelper(objects, _compilerOptions.GetEmitScriptTarget());
    }

    /// <summary>Go: <c>func (ch *objectRestSpreadTransformer) chunkObjectLiteralElements(list *ast.NodeList) []*ast.Node</c>。</summary>
    private Node[] ChunkObjectLiteralElements(NodeList? list)
    {
        if (list is null || (list.Nodes?.Length ?? 0) == 0)
        {
            return [];
        }

        var elements = list.Nodes ?? [];
        var chunkObject = new List<Node>();
        var objects = new List<Node>();
        foreach (var e in elements)
        {
            if (e.Kind == Kind.SpreadAssignment)
            {
                if (chunkObject.Count > 0)
                {
                    objects.Add(Factory().NewObjectLiteralExpression(Factory().NewNodeList(chunkObject.ToArray()), false));
                    chunkObject = [];
                }

                var target = e.Expression();
                objects.Add(Visitor().VisitNode(target)!);
            }
            else
            {
                Node? elem;
                if (e.Kind == Kind.PropertyAssignment)
                {
                    elem = Factory().NewPropertyAssignment(null, e.Name(), null, null, Visitor().VisitNode(e.Initializer()));
                }
                else
                {
                    elem = Visitor().VisitNode(e);
                }

                chunkObject.Add(elem!);
            }
        }

        if (chunkObject.Count > 0)
        {
            objects.Add(Factory().NewObjectLiteralExpression(Factory().NewNodeList(chunkObject.ToArray()), false));
        }

        return objects.ToArray();
    }
}

public static partial class EsTransforms
{
    /// <summary>Go: <c>func newObjectRestSpreadTransformer(opts *transformers.TransformOptions) *transformers.Transformer</c>。</summary>
    internal static Transformer? NewObjectRestSpreadTransformer(TransformOptions opts)
    {
        var tx = new ObjectRestSpreadTransformer(opts.CompilerOptions!);
        return tx.NewTransformer(tx.Visit, opts.Context);
    }
}
