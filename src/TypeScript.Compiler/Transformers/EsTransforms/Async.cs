// ported from: internal/transformers/estransforms/async.go:1-985（async/await 降级：__awaiter 包裹、super 访问重写、
//              `arguments` 词法捕获、参数列表重写、变量提升；含文件末尾的 4 个包级辅助函数 async.go:899-985）
//
// 与 Go 的差异（C# 语言特性，逐条）：
// 1) Go 用**值内嵌** `superAccessState` 复用 super 访问跟踪；C# 已继承 `Transformer`（无多继承），改为**组合**字段
//    `_super`（类型 `SuperAccessState` 见 estransforms/utilities.go 的移植，由 T5 落地）。调用点：
//    `tx.trackSuperAccess(x)` → `_super.TrackSuperAccess(x)`、`tx.capturedSuperProperties` → `_super._capturedSuperProperties`
//    等（字段名按 docs/architecture.md §2：Go 未导出字段 → `_camelCase` + `internal`）。
// 2) Go 的 `defer` → `try/finally`（`Visit`、`VisitArrowFunction` 各一处；Go 的 defer 在函数返回时执行，语义等价）。
// 3) Go 的 `doWithContext(flags, (*asyncTransformer).visitXxx, node)`（方法值）→ C# `Func<Node, Node?>` 方法组。
// 4) Go 的 `node.Parameters()`（`*Node` 上的访问器）在 C# 没有对应方法（docs/ast.md §2：只有 `ParameterList()`），
//    本文件用私有静态 `Parameters(Node)` 包装；语义相同（NodeList 为 nil 时 Go panic、C# NRE）。
// 5) Go 的 `slices.ContainsFunc` → 显式循环（docs/architecture.md §3.8：热路径不引入委托分配）。
// 6) Go 的 `collections.Set[string]{}` → `new Set<string>()`（Collections/Set.cs）；`OrderedSet[string]{}` → `new OrderedSet<string>()`。
// 7) `type asyncContextFlags int` + `1 << iota` 常量 → `[Flags] enum AsyncContextFlags`（数值与 Go 一致，
//    成员名 = Go 常量名去 `asyncContext` 前缀）。
// 8) 包级函数（Go 无接收者）→ 与包同名的 `public static partial class EsTransforms`（docs/architecture.md §2），
//    本文件里是 async.go 的 4 个辅助函数 + `newAsyncTransformer`。
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

/// <summary>Go: <c>type asyncContextFlags int</c>（async.go:14-17）。</summary>
[Flags]
internal enum AsyncContextFlags
{
    /// <summary>Go: <c>asyncContextNonTopLevel</c>。</summary>
    NonTopLevel = 1 << 0,
    /// <summary>Go: <c>asyncContextHasLexicalThis</c>。</summary>
    HasLexicalThis = 1 << 1,
}

/// <summary>
/// Go: <c>type lexicalArgumentsInfo struct</c>（async.go:19-22）。
/// Go 里是值类型（`savedLexicalArguments := tx.lexicalArguments` 是逐字段拷贝），故 C# 侧保持 struct 值语义。
/// </summary>
internal struct LexicalArgumentsInfo
{
    /// <summary>Go: <c>binding</c>。</summary>
    internal Identifier? _binding;
    /// <summary>Go: <c>used</c>。</summary>
    internal bool _used;
}

/// <summary>Go: <c>type asyncTransformer struct</c>（async.go:24-34）。</summary>
internal sealed class AsyncTransformer : Transformer
{
    private AsyncContextFlags _contextFlags;
    private Set<string>? _enclosingFunctionParameterNames;
    private LexicalArgumentsInfo _lexicalArguments;

    /// <summary>Go: 内嵌字段 <c>superAccessState</c>（见文件头差异 1）。工厂方法在另一个类里初始化它 → internal。</summary>
    internal SuperAccessState _super = new();

    internal NodeVisitor? _asyncBodyVisitor;
    internal NodeVisitor? _fallbackNodeVisitor;

    /// <summary>Go: <c>node.Parameters()</c>（见文件头差异 4）。</summary>
    private static Node[] Parameters(Node node) => node.ParameterList()!.Nodes!;

    /// <summary>Go: <c>func (tx *asyncTransformer) visitSourceFile</c>（async.go:41-51）。</summary>
    private Node? VisitSourceFile(SourceFile node)
    {
        if (node.IsDeclarationFile)
        {
            return node;
        }

        SetContextFlag(AsyncContextFlags.NonTopLevel, false);
        SetContextFlag(AsyncContextFlags.HasLexicalThis, false);
        var visited = Visitor().VisitEachChild(node);
        EmitContext().AddEmitHelper(visited!, EmitContext().ReadEmitHelpers());
        return visited;
    }

    /// <summary>Go: <c>func (tx *asyncTransformer) setContextFlag</c>（async.go:53-59）。</summary>
    private void SetContextFlag(AsyncContextFlags flag, bool val)
    {
        if (val)
        {
            _contextFlags |= flag;
        }
        else
        {
            _contextFlags &= ~flag;
        }
    }

    /// <summary>Go: <c>func (tx *asyncTransformer) inContext</c>（async.go:61-63）。</summary>
    private bool InContext(AsyncContextFlags flags) => (_contextFlags & flags) != 0;

    /// <summary>Go: <c>func (tx *asyncTransformer) inTopLevelContext</c>（async.go:65-67）。</summary>
    private bool InTopLevelContext() => !InContext(AsyncContextFlags.NonTopLevel);

    /// <summary>Go: <c>func (tx *asyncTransformer) inHasLexicalThisContext</c>（async.go:69-71）。</summary>
    private bool InHasLexicalThisContext() => InContext(AsyncContextFlags.HasLexicalThis);

    /// <summary>Go: <c>func (tx *asyncTransformer) doWithContext</c>（async.go:73-82）。</summary>
    private Node? DoWithContext(AsyncContextFlags flags, Func<Node, Node?> cb, Node node)
    {
        var flagsToSet = flags & ~_contextFlags;
        if (flagsToSet != 0)
        {
            SetContextFlag(flagsToSet, true);
            var result = cb(node);
            SetContextFlag(flagsToSet, false);
            return result;
        }
        return cb(node);
    }

    /// <summary>Go: <c>func (tx *asyncTransformer) visitDefault</c>（async.go:84-86）。</summary>
    private Node? VisitDefault(Node node) => Visitor().VisitEachChild(node);

    /// <summary>Go: <c>func (tx *asyncTransformer) fallbackVisitor</c>（async.go:88-113）。</summary>
    private Node? FallbackVisitor(Node node)
    {
        if (_super._capturedSuperProperties is null && _lexicalArguments._binding is null)
        {
            return node;
        }
        _super.TrackSuperAccess(node);
        switch (node.Kind)
        {
            case Kind.FunctionExpression:
            case Kind.FunctionDeclaration:
            case Kind.MethodDeclaration:
            case Kind.GetAccessor:
            case Kind.SetAccessor:
            case Kind.Constructor:
                return node;
            case Kind.Parameter:
            case Kind.BindingElement:
            case Kind.VariableDeclaration:
                // fall through to visitEachChild
                break;
            case Kind.Identifier:
                if (_lexicalArguments._binding is not null &&
                    node.Text() == "arguments" &&
                    !IsIdentifierName(node) &&
                    !IsLabelName(node))
                {
                    _lexicalArguments._used = true;
                    return _lexicalArguments._binding;
                }
                break;
        }
        return _fallbackNodeVisitor!.VisitEachChild(node);
    }

    /// <summary>Go: <c>func (tx *asyncTransformer) visitFallback</c>（async.go:115-117）。</summary>
    internal Node? VisitFallback(Node node) => FallbackVisitor(node);

    /// <summary>Go: <c>func (tx *asyncTransformer) visit</c>（async.go:119-147）。工厂方法用方法组传入 → internal。</summary>
    internal Node? Visit(Node node)
    {
        // Go: `if ... { tx.setContextFlag(...); defer tx.setContextFlag(..., true) }` → try/finally。
        var restoreHasLexicalThis = false;
        if ((EmitContext().EmitFlags(node) & EmitFlags.NoLexicalThis) != 0 && InHasLexicalThisContext())
        {
            SetContextFlag(AsyncContextFlags.HasLexicalThis, false);
            restoreHasLexicalThis = true;
        }
        try
        {
            if ((node.SubtreeFacts & (SubtreeFacts.ContainsAnyAwait | SubtreeFacts.ContainsAwait)) == 0)
            {
                return FallbackVisitor(node);
            }
            _super.TrackSuperAccess(node);
            switch (node.Kind)
            {
                case Kind.AsyncKeyword:
                    // ES2017 async modifier should be elided for targets < ES2017
                    return null;
                case Kind.SourceFile:
                    return VisitSourceFile(node.AsSourceFile()!);
                case Kind.AwaitExpression:
                    return VisitAwaitExpression(node.AsAwaitExpression()!);
                case Kind.MethodDeclaration:
                    return DoWithContext(AsyncContextFlags.NonTopLevel | AsyncContextFlags.HasLexicalThis, VisitMethodDeclaration, node);
                case Kind.FunctionDeclaration:
                    return DoWithContext(AsyncContextFlags.NonTopLevel | AsyncContextFlags.HasLexicalThis, VisitFunctionDeclaration, node);
                case Kind.FunctionExpression:
                    return DoWithContext(AsyncContextFlags.NonTopLevel | AsyncContextFlags.HasLexicalThis, VisitFunctionExpression, node);
                case Kind.ArrowFunction:
                    return DoWithContext(AsyncContextFlags.NonTopLevel, VisitArrowFunction, node);
                case Kind.GetAccessor:
                    return DoWithContext(AsyncContextFlags.NonTopLevel | AsyncContextFlags.HasLexicalThis, VisitGetAccessorDeclaration, node);
                case Kind.SetAccessor:
                    return DoWithContext(AsyncContextFlags.NonTopLevel | AsyncContextFlags.HasLexicalThis, VisitSetAccessorDeclaration, node);
                case Kind.Constructor:
                    return DoWithContext(AsyncContextFlags.NonTopLevel | AsyncContextFlags.HasLexicalThis, VisitConstructorDeclaration, node);
                case Kind.ClassDeclaration:
                case Kind.ClassExpression:
                    return DoWithContext(AsyncContextFlags.NonTopLevel | AsyncContextFlags.HasLexicalThis, VisitDefault, node);
                default:
                    return Visitor().VisitEachChild(node);
            }
        }
        finally
        {
            if (restoreHasLexicalThis)
            {
                SetContextFlag(AsyncContextFlags.HasLexicalThis, true);
            }
        }
    }

    /// <summary>Go: <c>func (tx *asyncTransformer) visitAsyncBodyNode</c>（async.go:149-175）。</summary>
    internal Node? VisitAsyncBodyNode(Node node)
    {
        if (IsNodeWithPossibleHoistedDeclaration(node))
        {
            switch (node.Kind)
            {
                case Kind.VariableStatement:
                    return VisitVariableStatementInAsyncBody(node);
                case Kind.ForStatement:
                    return VisitForStatementInAsyncBody(node.AsForStatement()!);
                case Kind.ForInStatement:
                    return VisitForInStatementInAsyncBody(node.AsForInOrOfStatement()!);
                case Kind.ForOfStatement:
                    return VisitForOfStatementInAsyncBody(node.AsForInOrOfStatement()!);
                case Kind.CatchClause:
                    return VisitCatchClauseInAsyncBody(node.AsCatchClause()!);
                case Kind.Block:
                case Kind.SwitchStatement:
                case Kind.CaseBlock:
                case Kind.CaseClause:
                case Kind.DefaultClause:
                case Kind.TryStatement:
                case Kind.DoStatement:
                case Kind.WhileStatement:
                case Kind.IfStatement:
                case Kind.WithStatement:
                case Kind.LabeledStatement:
                    return _asyncBodyVisitor!.VisitEachChild(node);
            }
        }
        return Visit(node);
    }

    /// <summary>Go: <c>func (tx *asyncTransformer) visitCatchClauseInAsyncBody</c>（async.go:177-200）。</summary>
    private Node? VisitCatchClauseInAsyncBody(CatchClause node)
    {
        var catchClauseNames = new Set<string>();
        if (node.VariableDeclaration is not null)
        {
            RecordDeclarationName(node.VariableDeclaration, catchClauseNames);
        }

        // names declared in a catch variable are block scoped
        Set<string>? catchClauseUnshadowedNames = null;
        foreach (var escapedName in catchClauseNames.Keys())
        {
            if (_enclosingFunctionParameterNames is not null && _enclosingFunctionParameterNames.Has(escapedName))
            {
                catchClauseUnshadowedNames ??= _enclosingFunctionParameterNames.Clone();
                catchClauseUnshadowedNames.Delete(escapedName);
            }
        }

        if (catchClauseUnshadowedNames is not null)
        {
            var savedEnclosingFunctionParameterNames = _enclosingFunctionParameterNames;
            _enclosingFunctionParameterNames = catchClauseUnshadowedNames;
            var result = _asyncBodyVisitor!.VisitEachChild(node);
            _enclosingFunctionParameterNames = savedEnclosingFunctionParameterNames;
            return result;
        }
        return _asyncBodyVisitor!.VisitEachChild(node);
    }

    /// <summary>Go: <c>func (tx *asyncTransformer) visitVariableStatementInAsyncBody</c>（async.go:202-211）。</summary>
    private Node? VisitVariableStatementInAsyncBody(Node node)
    {
        var declList = node.AsVariableStatement()!.DeclarationList;
        if (IsVariableDeclarationListWithCollidingName(declList))
        {
            var expression = VisitVariableDeclarationListWithCollidingNames(declList!.AsVariableDeclarationList()!, false);
            if (expression is not null)
            {
                return Factory().NewExpressionStatement(expression);
            }
            return null;
        }
        return Visitor().VisitEachChild(node);
    }

    /// <summary>Go: <c>func (tx *asyncTransformer) visitForInStatementInAsyncBody</c>（async.go:213-229）。</summary>
    private Node? VisitForInStatementInAsyncBody(ForInOrOfStatement node)
    {
        Node? visitedInitializer;
        if (IsVariableDeclarationListWithCollidingName(node.Initializer))
        {
            visitedInitializer = VisitVariableDeclarationListWithCollidingNames(node.Initializer!.AsVariableDeclarationList()!, true);
        }
        else
        {
            visitedInitializer = Visitor().VisitNode(node.Initializer);
        }

        return Factory().UpdateForInOrOfStatement(
            node,
            null, /*awaitModifier*/
            visitedInitializer,
            Visitor().VisitNode(node.Expression),
            _asyncBodyVisitor!.VisitEmbeddedStatement(node.Statement)
        );
    }

    /// <summary>Go: <c>func (tx *asyncTransformer) visitForOfStatementInAsyncBody</c>（async.go:231-249）。</summary>
    private Node? VisitForOfStatementInAsyncBody(ForInOrOfStatement node)
    {
        Node? visitedInitializer;
        if (IsVariableDeclarationListWithCollidingName(node.Initializer))
        {
            visitedInitializer = VisitVariableDeclarationListWithCollidingNames(node.Initializer!.AsVariableDeclarationList()!, true);
        }
        else
        {
            visitedInitializer = Visitor().VisitNode(node.Initializer);
        }

        return Factory().UpdateForInOrOfStatement(
            node,
            Visitor().VisitNode(node.AwaitModifier),
            visitedInitializer,
            Visitor().VisitNode(node.Expression),
            _asyncBodyVisitor!.VisitEmbeddedStatement(node.Statement)
        );
    }

    /// <summary>Go: <c>func (tx *asyncTransformer) visitForStatementInAsyncBody</c>（async.go:251-269）。</summary>
    private Node? VisitForStatementInAsyncBody(ForStatement node)
    {
        var initializer = node.Initializer;
        Node? visitedInitializer;
        if (initializer is not null && IsVariableDeclarationListWithCollidingName(initializer))
        {
            visitedInitializer = VisitVariableDeclarationListWithCollidingNames(initializer.AsVariableDeclarationList()!, false);
        }
        else
        {
            visitedInitializer = Visitor().VisitNode(node.Initializer);
        }

        return Factory().UpdateForStatement(
            node,
            visitedInitializer,
            Visitor().VisitNode(node.Condition),
            Visitor().VisitNode(node.Incrementor),
            _asyncBodyVisitor!.VisitEmbeddedStatement(node.Statement)
        );
    }

    /// <summary>
    /// Go: <c>func (tx *asyncTransformer) visitAwaitExpression</c>（async.go:271-292）。
    ///
    /// This function will be called any time a ES2017 await expression is encountered.
    /// </summary>
    private Node? VisitAwaitExpression(AwaitExpression node)
    {
        // do not downlevel a top-level await as it is module syntax...
        if (InTopLevelContext())
        {
            return Visitor().VisitEachChild(node);
        }
        var yieldExpr = Factory().NewYieldExpression(
            null, /*asteriskToken*/
            Visitor().VisitNode(node.Expression)
        );
        yieldExpr.Loc = node.Loc;
        EmitContext().SetOriginal(yieldExpr, node);
        return yieldExpr;
    }

    /// <summary>Go: <c>func (tx *asyncTransformer) visitConstructorDeclaration</c>（async.go:294-307）。</summary>
    private Node? VisitConstructorDeclaration(Node node)
    {
        var decl = node.AsConstructorDeclaration()!;
        var savedLexicalArguments = _lexicalArguments;
        _lexicalArguments = new LexicalArgumentsInfo();
        var updated = Factory().UpdateConstructorDeclaration(
            decl,
            Visitor().VisitModifiers(decl.Modifiers()),
            null, /*typeParameters*/
            EmitContext().VisitParameters(decl.Parameters!, Visitor()),
            null, /*returnType*/
            null, /*fullSignature*/
            TransformMethodBody(node)
        );
        _lexicalArguments = savedLexicalArguments;
        return updated;
    }

    /// <summary>
    /// Go: <c>func (tx *asyncTransformer) visitMethodDeclaration</c>（async.go:309-345）。
    ///
    /// This function will be called when one of the following conditions are met:
    /// - The node is marked as async
    /// </summary>
    private Node? VisitMethodDeclaration(Node node)
    {
        var decl = node.AsMethodDeclaration()!;
        var functionFlags = GetFunctionFlags(node);
        var savedLexicalArguments = _lexicalArguments;
        _lexicalArguments = new LexicalArgumentsInfo();

        NodeList? parameters;
        Node? body;
        if ((functionFlags & FunctionFlags.Async) != 0)
        {
            parameters = TransformAsyncFunctionParameterList(node);
            body = TransformAsyncFunctionBody(node, parameters);
        }
        else
        {
            parameters = EmitContext().VisitParameters(decl.Parameters!, Visitor());
            body = TransformMethodBody(node);
        }

        var updated = Factory().UpdateMethodDeclaration(
            decl,
            Visitor().VisitModifiers(decl.Modifiers()),
            decl.AsteriskToken,
            decl.Name(),
            null, /*postfixToken*/
            null, /*typeParameters*/
            parameters,
            null, /*returnType*/
            null, /*fullSignature*/
            body
        );
        _lexicalArguments = savedLexicalArguments;
        return updated;
    }

    /// <summary>Go: <c>func (tx *asyncTransformer) visitGetAccessorDeclaration</c>（async.go:347-363）。</summary>
    private Node? VisitGetAccessorDeclaration(Node node)
    {
        var decl = node.AsGetAccessorDeclaration()!;
        var savedLexicalArguments = _lexicalArguments;
        _lexicalArguments = new LexicalArgumentsInfo();
        var updated = Factory().UpdateGetAccessorDeclaration(
            decl,
            Visitor().VisitModifiers(decl.Modifiers()),
            decl.Name(),
            null, /*typeParameters*/
            EmitContext().VisitParameters(decl.Parameters!, Visitor()),
            null, /*returnType*/
            null, /*fullSignature*/
            TransformMethodBody(node)
        );
        _lexicalArguments = savedLexicalArguments;
        return updated;
    }

    /// <summary>Go: <c>func (tx *asyncTransformer) visitSetAccessorDeclaration</c>（async.go:365-381）。</summary>
    private Node? VisitSetAccessorDeclaration(Node node)
    {
        var decl = node.AsSetAccessorDeclaration()!;
        var savedLexicalArguments = _lexicalArguments;
        _lexicalArguments = new LexicalArgumentsInfo();
        var updated = Factory().UpdateSetAccessorDeclaration(
            decl,
            Visitor().VisitModifiers(decl.Modifiers()),
            decl.Name(),
            null, /*typeParameters*/
            EmitContext().VisitParameters(decl.Parameters!, Visitor()),
            null, /*returnType*/
            null, /*fullSignature*/
            TransformMethodBody(node)
        );
        _lexicalArguments = savedLexicalArguments;
        return updated;
    }

    /// <summary>
    /// Go: <c>func (tx *asyncTransformer) visitFunctionDeclaration</c>（async.go:383-417）。
    ///
    /// This function will be called when one of the following conditions are met:
    /// - The node is marked async
    /// </summary>
    private Node? VisitFunctionDeclaration(Node node)
    {
        var decl = node.AsFunctionDeclaration()!;
        var functionFlags = GetFunctionFlags(node);
        var savedLexicalArguments = _lexicalArguments;
        _lexicalArguments = new LexicalArgumentsInfo();

        NodeList? parameters;
        Node? body;
        if ((functionFlags & FunctionFlags.Async) != 0)
        {
            parameters = TransformAsyncFunctionParameterList(node);
            body = TransformAsyncFunctionBody(node, parameters);
        }
        else
        {
            parameters = EmitContext().VisitParameters(decl.Parameters!, Visitor());
            body = EmitContext().VisitFunctionBody(node.Body()!, Visitor());
        }

        var updated = Factory().UpdateFunctionDeclaration(
            decl,
            Visitor().VisitModifiers(decl.Modifiers()),
            decl.AsteriskToken,
            Visitor().VisitNode(decl.Name()),
            null, /*typeParameters*/
            parameters,
            null, /*returnType*/
            null, /*fullSignature*/
            body
        );
        _lexicalArguments = savedLexicalArguments;
        return updated;
    }

    /// <summary>
    /// Go: <c>func (tx *asyncTransformer) visitFunctionExpression</c>（async.go:419-453）。
    ///
    /// This function will be called when one of the following conditions are met:
    /// - The node is marked async
    /// </summary>
    private Node? VisitFunctionExpression(Node node)
    {
        var decl = node.AsFunctionExpression()!;
        var functionFlags = GetFunctionFlags(node);
        var savedLexicalArguments = _lexicalArguments;
        _lexicalArguments = new LexicalArgumentsInfo();

        NodeList? parameters;
        Node? body;
        if ((functionFlags & FunctionFlags.Async) != 0)
        {
            parameters = TransformAsyncFunctionParameterList(node);
            body = TransformAsyncFunctionBody(node, parameters);
        }
        else
        {
            parameters = EmitContext().VisitParameters(decl.Parameters!, Visitor());
            body = EmitContext().VisitFunctionBody(node.Body()!, Visitor());
        }

        var updated = Factory().UpdateFunctionExpression(
            decl,
            Visitor().VisitModifiers(decl.Modifiers()),
            decl.AsteriskToken,
            Visitor().VisitNode(decl.Name()),
            null, /*typeParameters*/
            parameters,
            null, /*returnType*/
            null, /*fullSignature*/
            body
        );
        _lexicalArguments = savedLexicalArguments;
        return updated;
    }

    /// <summary>
    /// Go: <c>func (tx *asyncTransformer) visitArrowFunction</c>（async.go:455-497）。
    ///
    /// This function will be called when one of the following conditions are met:
    /// - The node is marked async
    /// </summary>
    private Node? VisitArrowFunction(Node node)
    {
        // `arguments` in class static blocks is always an error, but we preserve Strada's emit
        // behavior for baseline compatibility. In Strada, checker-based `isArgumentsLocalBinding`
        // returns false for `arguments` in static blocks (since the binding doesn't exist due to
        // the error), so the async transform leaves them untouched.
        var restoreLexicalArguments = false;
        var savedLexicalArguments = _lexicalArguments;
        if ((EmitContext().EmitFlags(node) & EmitFlags.NoLexicalArguments) != 0)
        {
            _lexicalArguments = new LexicalArgumentsInfo();
            restoreLexicalArguments = true;
        }
        try
        {
            var decl = node.AsArrowFunction()!;
            var functionFlags = GetFunctionFlags(node);

            NodeList? parameters;
            Node? body;
            if ((functionFlags & FunctionFlags.Async) != 0)
            {
                parameters = TransformAsyncFunctionParameterList(node);
                body = TransformAsyncFunctionBody(node, parameters);
            }
            else
            {
                parameters = EmitContext().VisitParameters(decl.Parameters!, Visitor());
                body = EmitContext().VisitFunctionBody(node.Body()!, Visitor());
            }

            return Factory().UpdateArrowFunction(
                decl,
                Visitor().VisitModifiers(decl.Modifiers()),
                null, /*typeParameters*/
                parameters,
                null, /*returnType*/
                null, /*fullSignature*/
                decl.EqualsGreaterThanToken,
                body
            );
        }
        finally
        {
            // Go: `defer func() { tx.lexicalArguments = savedLexicalArguments }()`（仅在设置了
            // NoLexicalArguments 分支里注册；savedLexicalArguments 无论如何都是进入时的快照）。
            if (restoreLexicalArguments)
            {
                _lexicalArguments = savedLexicalArguments;
            }
        }
    }

    /// <summary>Go: <c>func (tx *asyncTransformer) recordDeclarationName</c>（async.go:499-511）。</summary>
    private void RecordDeclarationName(Node node, Set<string> names)
    {
        var name = node.Name();
        if (name is null)
        {
            return;
        }
        if (IsIdentifier(name))
        {
            names.Add(name.Text());
        }
        else if (IsBindingPattern(name))
        {
            foreach (var element in name.AsBindingPattern()!.Elements!.Nodes ?? [])
            {
                if (!IsOmittedExpression(element))
                {
                    RecordDeclarationName(element, names);
                }
            }
        }
    }

    /// <summary>Go: <c>func (tx *asyncTransformer) isVariableDeclarationListWithCollidingName</c>（async.go:513-518）。</summary>
    private bool IsVariableDeclarationListWithCollidingName(Node? node)
    {
        if (node is null || !IsVariableDeclarationList(node) || (node.Flags & NodeFlags.BlockScoped) != 0)
        {
            return false;
        }
        // Go: slices.ContainsFunc(node.Declarations.Nodes, tx.collidesWithParameterName)
        foreach (var declaration in node.AsVariableDeclarationList()!.Declarations!.Nodes ?? [])
        {
            if (CollidesWithParameterName(declaration))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Go: <c>func (tx *asyncTransformer) visitVariableDeclarationListWithCollidingNames</c>（async.go:520-548）。</summary>
    private Node? VisitVariableDeclarationListWithCollidingNames(VariableDeclarationList node, bool hasReceiver)
    {
        HoistVariableDeclarationList(node);

        var variables = new List<Node>();
        foreach (var decl in node.Declarations!.Nodes ?? [])
        {
            if (decl.AsVariableDeclaration()!.Initializer is not null)
            {
                variables.Add(decl);
            }
        }

        if (variables.Count == 0)
        {
            if (hasReceiver)
            {
                var name = node.Declarations!.Nodes![0].Name()!;
                Node target;
                if (IsBindingPattern(name))
                {
                    target = ConvertBindingPatternToAssignmentPattern(EmitContext(), name.AsBindingPattern()!)!;
                }
                else
                {
                    target = name;
                }
                return Visitor().VisitNode(target);
            }
            return null;
        }

        var expressions = new Node[variables.Count];
        for (var i = 0; i < variables.Count; i++)
        {
            expressions[i] = TransformInitializedVariable(variables[i].AsVariableDeclaration()!)!;
        }
        return Factory().InlineExpressions(expressions);
    }

    /// <summary>Go: <c>func (tx *asyncTransformer) hoistVariableDeclarationList</c>（async.go:550-554）。</summary>
    private void HoistVariableDeclarationList(VariableDeclarationList node)
    {
        foreach (var decl in node.Declarations!.Nodes ?? [])
        {
            HoistVariable(decl);
        }
    }

    /// <summary>Go: <c>func (tx *asyncTransformer) hoistVariable</c>（async.go:556-568）。</summary>
    private void HoistVariable(Node node)
    {
        var name = node.Name();
        if (name is null)
        {
            return;
        }
        if (IsIdentifier(name))
        {
            EmitContext().AddVariableDeclaration(name);
        }
        else if (IsBindingPattern(name))
        {
            foreach (var element in name.AsBindingPattern()!.Elements!.Nodes ?? [])
            {
                if (!IsOmittedExpression(element))
                {
                    HoistVariable(element);
                }
            }
        }
    }

    /// <summary>Go: <c>func (tx *asyncTransformer) transformInitializedVariable</c>（async.go:570-579）。</summary>
    private Node? TransformInitializedVariable(VariableDeclaration node)
    {
        Node target;
        if (IsBindingPattern(node.Name()!))
        {
            target = ConvertBindingPatternToAssignmentPattern(EmitContext(), node.Name()!.AsBindingPattern()!)!;
        }
        else
        {
            target = node.Name()!;
        }
        var converted = Factory().NewAssignmentExpression(target, node.Initializer!);
        EmitContext().SetSourceMapRange(converted, node.Loc);
        return Visitor().VisitNode(converted);
    }

    /// <summary>Go: <c>func (tx *asyncTransformer) collidesWithParameterName</c>（async.go:581-595）。</summary>
    private bool CollidesWithParameterName(Node node)
    {
        var name = node.Name();
        if (name is null)
        {
            return false;
        }
        if (IsIdentifier(name))
        {
            return _enclosingFunctionParameterNames is not null && _enclosingFunctionParameterNames.Has(name.Text());
        }
        if (IsBindingPattern(name))
        {
            foreach (var element in name.AsBindingPattern()!.Elements!.Nodes ?? [])
            {
                if (!IsOmittedExpression(element) && CollidesWithParameterName(element))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>Go: <c>func (tx *asyncTransformer) transformMethodBody</c>（async.go:597-661）。</summary>
    private Node? TransformMethodBody(Node node)
    {
        var savedCapturedSuperProperties = _super._capturedSuperProperties;
        var savedHasSuperElementAccess = _super._hasSuperElementAccess;
        var savedHasSuperPropertyAssignment = _super._hasSuperPropertyAssignment;
        var savedSuperBinding = _super._superBinding;
        var savedSuperIndexBinding = _super._superIndexBinding;
        _super._capturedSuperProperties = new OrderedSet<string>();
        _super._hasSuperElementAccess = false;
        _super._hasSuperPropertyAssignment = false;
        _super._superBinding = (Identifier)Factory().NewUniqueNameEx("_super", new AutoGenerateOptions { Flags = GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.FileLevel });
        _super._superIndexBinding = (Identifier)Factory().NewUniqueNameEx("_superIndex", new AutoGenerateOptions { Flags = GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.FileLevel });

        EmitContext().StartVariableEnvironment();
        var updated = EmitContext().VisitFunctionBody(node.Body()!, Visitor());

        // Minor optimization, emit `_super` helper to capture `super` access in an arrow.
        var emitSuperHelpers = (_super._capturedSuperProperties.Size() > 0 || _super._hasSuperElementAccess) &&
            (GetFunctionFlags(GetOriginalIfFunctionLike(node)) & FunctionFlags.AsyncGenerator) != FunctionFlags.AsyncGenerator;

        if (emitSuperHelpers)
        {
            if (_super._capturedSuperProperties.Size() > 0)
            {
                EmitContext().AddInitializationStatement(_super.CreateSuperAccessVariableStatement()!);
            }
        }

        var mergedStatements = EmitContext().EndAndMergeVariableEnvironmentList(updated!.StatementList());
        if (emitSuperHelpers && _super._hasSuperElementAccess && !updated.AsBlock()!.MultiLine)
        {
            var newBlock = Factory().NewBlock(mergedStatements, true);
            newBlock.Loc = updated.Loc;
            updated = newBlock;
        }
        else
        {
            updated = Factory().UpdateBlock(updated.AsBlock()!, mergedStatements, updated.AsBlock()!.MultiLine);
        }

        if (emitSuperHelpers && _super._hasSuperElementAccess)
        {
            if (_super._hasSuperPropertyAssignment)
            {
                EmitContext().AddEmitHelper(updated, AdvancedAsyncSuperHelper);
            }
            else
            {
                EmitContext().AddEmitHelper(updated, AsyncSuperHelper);
            }
        }

        _super._capturedSuperProperties = savedCapturedSuperProperties;
        _super._hasSuperElementAccess = savedHasSuperElementAccess;
        _super._hasSuperPropertyAssignment = savedHasSuperPropertyAssignment;
        _super._superBinding = savedSuperBinding;
        _super._superIndexBinding = savedSuperIndexBinding;
        return updated;
    }

    /// <summary>Go: <c>func (tx *asyncTransformer) createCaptureArgumentsStatement</c>（async.go:663-675）。</summary>
    private Node? CreateCaptureArgumentsStatement()
    {
        var variable = Factory().NewVariableDeclaration(
            _lexicalArguments._binding!,
            null,
            null,
            Factory().NewIdentifier("arguments")
        );
        var declList = Factory().NewVariableDeclarationList(Factory().NewNodeList([variable]), NodeFlags.None);
        var statement = Factory().NewVariableStatement(null, declList);
        EmitContext().AddEmitFlags(statement, EmitFlags.StartOnNewLine | EmitFlags.CustomPrologue);
        return statement;
    }

    /// <summary>Go: <c>func (tx *asyncTransformer) transformAsyncFunctionParameterList</c>（async.go:677-717）。</summary>
    private NodeList TransformAsyncFunctionParameterList(Node node)
    {
        if (IsSimpleParameterList(Parameters(node)))
        {
            return EmitContext().VisitParameters(node.ParameterList()!, Visitor())!;
        }

        var newParameters = new List<Node>();
        foreach (var parameter in Parameters(node))
        {
            var param = parameter.AsParameterDeclaration()!;
            if (param.Initializer is not null || param.DotDotDotToken is not null)
            {
                // for an arrow function, capture the remaining arguments in a rest parameter.
                // for any other function/method this isn't necessary as we can just use `arguments`.
                if (node.Kind == Kind.ArrowFunction)
                {
                    var restParameter = Factory().NewParameterDeclaration(
                        null,
                        Factory().NewToken(Kind.DotDotDotToken),
                        Factory().NewUniqueNameEx("args", new AutoGenerateOptions { Flags = GeneratedIdentifierFlags.ReservedInNestedScopes }),
                        null,
                        null,
                        null
                    );
                    newParameters.Add(restParameter);
                }
                break;
            }
            // for arrow functions we capture fixed parameters to forward to `__awaiter`. For all other functions
            // we add fixed parameters to preserve the function's `length` property.
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

    /// <summary>Go: <c>func (tx *asyncTransformer) transformAsyncFunctionBody</c>（async.go:719-879）。</summary>
    private Node? TransformAsyncFunctionBody(Node node, NodeList? outerParameters)
    {
        var isArrow = node.Kind == Kind.ArrowFunction;
        var savedCapturedSuperProperties = _super._capturedSuperProperties;
        var savedHasSuperElementAccess = _super._hasSuperElementAccess;
        var savedHasSuperPropertyAssignment = _super._hasSuperPropertyAssignment;
        var savedSuperBinding = _super._superBinding;
        var savedSuperIndexBinding = _super._superIndexBinding;
        if (!isArrow)
        {
            _super._capturedSuperProperties = new OrderedSet<string>();
            _super._hasSuperElementAccess = false;
            _super._hasSuperPropertyAssignment = false;
            _super._superBinding = (Identifier)Factory().NewUniqueNameEx("_super", new AutoGenerateOptions { Flags = GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.FileLevel });
            _super._superIndexBinding = (Identifier)Factory().NewUniqueNameEx("_superIndex", new AutoGenerateOptions { Flags = GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.FileLevel });
        }

        NodeList? innerParameters = null;
        if (!IsSimpleParameterList(Parameters(node)))
        {
            innerParameters = EmitContext().VisitParameters(node.ParameterList()!, Visitor());
        }

        var savedLexicalArguments = _lexicalArguments;
        var captureLexicalArguments = _lexicalArguments._binding is null;
        if (captureLexicalArguments)
        {
            _lexicalArguments = new LexicalArgumentsInfo
            {
                _binding = (Identifier)Factory().NewUniqueName("arguments"),
            };
        }

        Node? argumentsExpression = null;
        if (innerParameters is not null)
        {
            if (isArrow)
            {
                // `node` does not have a simple parameter list, so `outerParameters` refers to placeholders that are
                // forwarded to `innerParameters`, matching how they are introduced in `transformAsyncFunctionParameterList`.
                var parameterBindings = new List<Node>();
                var outerLen = outerParameters!.Nodes!.Length;
                var parameters = Parameters(node);
                for (var i = 0; i < parameters.Length; i++)
                {
                    if (i >= outerLen)
                    {
                        break;
                    }
                    var originalParameter = parameters[i].AsParameterDeclaration()!;
                    var outerParameter = outerParameters.Nodes![i].AsParameterDeclaration()!;
                    if (originalParameter.Initializer is not null || originalParameter.DotDotDotToken is not null)
                    {
                        parameterBindings.Add(Factory().NewSpreadElement(outerParameter.Name()));
                        break;
                    }
                    parameterBindings.Add(outerParameter.Name()!);
                }
                argumentsExpression = Factory().NewArrayLiteralExpression(Factory().NewNodeList(parameterBindings.ToArray()), false);
            }
            else
            {
                argumentsExpression = Factory().NewIdentifier("arguments");
            }
        }

        // An async function is emit as an outer function that calls an inner
        // generator function. To preserve lexical bindings, we pass the current
        // `this` and `arguments` objects to `__awaiter`. The generator function
        // passed to `__awaiter` is executed inside of the callback to the
        // promise constructor.

        var savedEnclosingFunctionParameterNames = _enclosingFunctionParameterNames;
        _enclosingFunctionParameterNames = new Set<string>();
        foreach (var parameter in Parameters(node))
        {
            RecordDeclarationName(parameter, _enclosingFunctionParameterNames);
        }

        var hasLexicalThis = InHasLexicalThisContext();

        var asyncBody = TransformAsyncFunctionBodyWorker(node.Body()!)!;
        asyncBody = Factory().UpdateBlock(
            asyncBody.AsBlock()!,
            EmitContext().EndAndMergeVariableEnvironmentList(asyncBody.StatementList()),
            asyncBody.AsBlock()!.MultiLine
        );

        // Substitute super property accesses with _super/_superIndex helpers
        var emitSuperHelpers = _super._capturedSuperProperties is not null &&
            (_super._capturedSuperProperties.Size() > 0 || _super._hasSuperElementAccess);
        if (emitSuperHelpers)
        {
            innerParameters = _super._superAccessVisitor!.VisitNodes(innerParameters);
            asyncBody = _super.SubstituteSuperAccessesInBody(asyncBody)!;
        }

        Node? result;
        if (!isArrow)
        {
            EmitContext().StartVariableEnvironment();

            // Minor optimization, emit `_super` helper to capture `super` access in an arrow.
            if (emitSuperHelpers)
            {
                if (_super._capturedSuperProperties!.Size() > 0)
                {
                    EmitContext().AddInitializationStatement(_super.CreateSuperAccessVariableStatement()!);
                }
            }

            if (captureLexicalArguments && _lexicalArguments._used)
            {
                EmitContext().AddInitializationStatement(CreateCaptureArgumentsStatement()!);
            }

            var statements = new Node[]
            {
                Factory().NewReturnStatement(
                    Factory().NewAwaiterHelper(
                        hasLexicalThis,
                        argumentsExpression,
                        innerParameters,
                        asyncBody
                    )
                ),
            };

            var block = Factory().NewBlock(
                EmitContext().EndAndMergeVariableEnvironmentList(Factory().NewNodeList(statements)),
                true
            );
            block.Loc = node.Body()!.Loc;

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

            result = block;
        }
        else
        {
            result = Factory().NewAwaiterHelper(
                hasLexicalThis,
                argumentsExpression,
                innerParameters,
                asyncBody
            );

            if (captureLexicalArguments && _lexicalArguments._used)
            {
                var block = EmitContext().ConvertToFunctionBlock(result, true /*multiLine*/);
                if (!IsBlock(result))
                {
                    EmitContext().SetOriginal(block.StatementList()!.Nodes![0], result);
                }
                result = Factory().UpdateBlock(
                    block.AsBlock()!,
                    EmitContext().MergeEnvironmentList(block.StatementList()!, [CreateCaptureArgumentsStatement()!]),
                    block.AsBlock()!.MultiLine
                );
            }
        }

        _enclosingFunctionParameterNames = savedEnclosingFunctionParameterNames;
        if (!isArrow)
        {
            _super._capturedSuperProperties = savedCapturedSuperProperties;
            _super._hasSuperElementAccess = savedHasSuperElementAccess;
            _super._hasSuperPropertyAssignment = savedHasSuperPropertyAssignment;
            _super._superBinding = savedSuperBinding;
            _super._superIndexBinding = savedSuperIndexBinding;
            _lexicalArguments = savedLexicalArguments;
        }
        else if (captureLexicalArguments && !_lexicalArguments._used)
        {
            // If we created a new binding but it wasn't used, restore the previous state.
            // If it was used, keep the binding alive so sibling arrows can reuse it
            // (the `var` declaration hoists to the enclosing function scope).
            _lexicalArguments = savedLexicalArguments;
        }
        else if (captureLexicalArguments)
        {
            // Keep the binding but clear the used flag so siblings don't re-emit the capture statement.
            _lexicalArguments._used = false;
        }
        return result;
    }

    /// <summary>Go: <c>func (tx *asyncTransformer) transformAsyncFunctionBodyWorker</c>（async.go:881-897）。</summary>
    private Node? TransformAsyncFunctionBodyWorker(Node body)
    {
        if (IsBlock(body))
        {
            return Factory().UpdateBlock(
                body.AsBlock()!,
                _asyncBodyVisitor!.VisitNodes(body.StatementList()),
                body.AsBlock()!.MultiLine
            );
        }
        // Convert expression body to block body with return statement
        var visited = _asyncBodyVisitor!.VisitNode(body);
        var ret = Factory().NewReturnStatement(visited);
        ret.Loc = body.Loc;
        var list = Factory().NewNodeList([ret]);
        list.Loc = body.Loc;
        var block = Factory().NewBlock(list, false /*multiLine*/);
        block.Loc = body.Loc;
        return block;
    }

    /// <summary>Go: <c>func (tx *asyncTransformer) getOriginalIfFunctionLike</c>（async.go:949-955）。</summary>
    private Node GetOriginalIfFunctionLike(Node node)
    {
        var original = EmitContext().MostOriginal(node);
        if (original is not null && IsFunctionLikeDeclaration(original))
        {
            return original;
        }
        return node;
    }
}

/// <summary>Go: 包级函数/类型构造（`estransforms` 包，async.go/forawait.go/using.go 共用）。</summary>
public static partial class EsTransforms
{
    /// <summary>Go: <c>func newAsyncTransformer(opts *transformers.TransformOptions) *transformers.Transformer</c>（async.go:36-44）。</summary>
    internal static Transformer? NewAsyncTransformer(TransformOptions opts)
    {
        var tx = new AsyncTransformer();
        var result = tx.NewTransformer(tx.Visit, opts.Context);
        tx._super.InitSuperAccessVisitor(tx.EmitContext(), tx.Factory());
        tx._asyncBodyVisitor = tx.EmitContext().NewNodeVisitor(tx.VisitAsyncBodyNode);
        tx._fallbackNodeVisitor = tx.EmitContext().NewNodeVisitor(tx.VisitFallback);
        return result;
    }

    /// <summary>
    /// Go: <c>func assignmentTargetContainsSuperProperty(node *ast.Node) bool</c>（async.go:899-929）。
    ///
    /// checks top-down whether an assignment target expression contains a super property or element access
    /// (super.x or super[x]). This avoids relying on parent pointers (IsAssignmentTarget) which may not be set
    /// on synthesized AST nodes from prior transforms.
    /// </summary>
    internal static bool AssignmentTargetContainsSuperProperty(Node node)
    {
        switch (node.Kind)
        {
            case Kind.PropertyAccessExpression:
            case Kind.ElementAccessExpression:
                return node.Expression()!.Kind == Kind.SuperKeyword;
            case Kind.ParenthesizedExpression:
                return AssignmentTargetContainsSuperProperty(node.AsParenthesizedExpression()!.Expression!);
            case Kind.ArrayLiteralExpression:
                // Go: slices.ContainsFunc(node.AsArrayLiteralExpression().Elements.Nodes, ...)
                foreach (var element in node.AsArrayLiteralExpression()!.Elements!.Nodes ?? [])
                {
                    if (AssignmentTargetContainsSuperProperty(element))
                    {
                        return true;
                    }
                }
                break;
            case Kind.ObjectLiteralExpression:
                foreach (var prop in node.AsObjectLiteralExpression()!.Properties!.Nodes ?? [])
                {
                    switch (prop.Kind)
                    {
                        case Kind.PropertyAssignment:
                            if (AssignmentTargetContainsSuperProperty(prop.AsPropertyAssignment()!.Initializer!))
                            {
                                return true;
                            }
                            break;
                        case Kind.ShorthandPropertyAssignment:
                            if (AssignmentTargetContainsSuperProperty(prop.AsShorthandPropertyAssignment()!.Name()!))
                            {
                                return true;
                            }
                            break;
                        case Kind.SpreadAssignment:
                            if (AssignmentTargetContainsSuperProperty(prop.AsSpreadAssignment()!.Expression!))
                            {
                                return true;
                            }
                            break;
                    }
                }
                break;
            case Kind.SpreadElement:
                return AssignmentTargetContainsSuperProperty(node.AsSpreadElement()!.Expression!);
        }
        return false;
    }

    /// <summary>Go: <c>func isUpdateExpression(node *ast.Node) bool</c>（async.go:931-947）。checks if a prefix/postfix unary expression is ++ or --.</summary>
    internal static bool IsUpdateExpression(Node node)
    {
        if (IsPrefixUnaryExpression(node))
        {
            var op = node.AsPrefixUnaryExpression()!.Operator;
            return op == Kind.PlusPlusToken || op == Kind.MinusMinusToken;
        }
        if (IsPostfixUnaryExpression(node))
        {
            var op = node.AsPostfixUnaryExpression()!.Operator;
            return op == Kind.PlusPlusToken || op == Kind.MinusMinusToken;
        }
        return false;
    }

    /// <summary>Go: <c>func isSimpleParameterList(params []*ast.Node) bool</c>（async.go:952-961）。checks if every parameter has no initializer and an Identifier name.</summary>
    internal static bool IsSimpleParameterList(Node[]? parameters)
    {
        foreach (var param in parameters ?? [])
        {
            var p = param.AsParameterDeclaration()!;
            if (p.Initializer is not null || !IsIdentifier(p.Name()!))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Go: <c>func isNodeWithPossibleHoistedDeclaration(node *ast.Node) bool</c>（async.go:963-985）。checks if a node could contain hoisted declarations.</summary>
    internal static bool IsNodeWithPossibleHoistedDeclaration(Node node)
    {
        switch (node.Kind)
        {
            case Kind.Block:
            case Kind.VariableStatement:
            case Kind.WithStatement:
            case Kind.IfStatement:
            case Kind.SwitchStatement:
            case Kind.CaseBlock:
            case Kind.CaseClause:
            case Kind.DefaultClause:
            case Kind.LabeledStatement:
            case Kind.ForStatement:
            case Kind.ForInStatement:
            case Kind.ForOfStatement:
            case Kind.DoStatement:
            case Kind.WhileStatement:
            case Kind.TryStatement:
            case Kind.CatchClause:
                return true;
        }
        return false;
    }
}
