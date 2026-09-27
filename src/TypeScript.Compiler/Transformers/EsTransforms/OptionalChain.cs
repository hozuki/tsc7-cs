// ported from: internal/transformers/estransforms/optionalchain.go（240 行，全部：optionalChainTransformer +
//               visit、visitCallExpression、visitParenthesizedExpression、visitPropertyOrElementAccessExpression、
//               visitDeleteExpression、visitNonOptionalExpression、visitOptionalExpression、
//               flattenResult/isNonNullChain/flattenChain/isCallChain、newOptionalChainTransformer）
//
// 与 Go 的差异（逐条）：
// 1) Go 的 `type optionalChainTransformer struct { transformers.Transformer }`（嵌入）→ C# 的
//    `internal sealed class OptionalChainTransformer : Transformer`（继承，见 Transformers/Transformer.cs 契约）。
// 2) Go 的包级类型 `flattenResult` → C# 的 `internal readonly struct FlattenResult`（未导出类型按 C# 习惯命名）：
//    Go 的 `%T{expr, links}` 结构体字面量 → 构造函数；字段 `expression`/`chain` → `_expression`/`_chain`
//    （docs/architecture.md §2：Go 未导出字段 → `_camelCase`）。
// 3) Go 的包级函数 `isNonNullChain`/`flattenChain`/`isCallChain` → `EsTransforms` 的 `internal static` 成员（§2）。
// 4) Go 的 `links = append([]*ast.Node{chain}, links...)`（每次前插）→ C# `links.Insert(0, chain)`（同样的前插语义与
//    同样的 O(n) 代价）。
// 5) Go 的 `chain = ast.SkipPartiallyEmittedExpressions(chain.Expression())`（Go 里结果永不为 nil，否则下一次循环
//    条件就 panic）→ C# 用 `!` 表达该不变量。
// 6) `expression.AsSyntheticReferenceExpression().ThisArg`/`segment.ArgumentList().Nodes` 在 Go 里可为 nil
//    （这两处 Go 会 panic：`NewFunctionCallCall` 的参数按值解引用）→ C# 用 `!` 保持同一不变量。
// 7) Go 的 `debug.Assert(cond)`（无消息）→ C# `Debug.Assert(cond)`（`params object?[]` 允许省略）。
// 8) Go 的 `printer.EFNoComments` → C# `EmitFlags.NoComments`（enum 成员名去掉 Go 的 `EF` 前缀）。
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Printer;
using static TypeScript.Ast.Ast;
using static TypeScript.Transformers.EsTransforms.EsTransforms;
using static TypeScript.Transformers.Transformers;

namespace TypeScript.Transformers.EsTransforms;

/// <summary>Go: <c>type flattenResult struct { expression *ast.Expression; chain []*ast.Node }</c>。</summary>
internal readonly struct FlattenResult
{
    internal readonly Node? _expression;
    internal readonly Node[] _chain;

    internal FlattenResult(Node? expression, Node[] chain)
    {
        _expression = expression;
        _chain = chain;
    }
}

internal sealed class OptionalChainTransformer : Transformer
{
    /// <summary>Go: <c>func (ch *optionalChainTransformer) visit(node *ast.Node) *ast.Node</c>。</summary>
    internal Node? Visit(Node node)
    {
        if ((node.SubtreeFacts & SubtreeFacts.ContainsOptionalChaining) == 0)
        {
            return node;
        }

        switch (node.Kind)
        {
            case Kind.CallExpression:
                return VisitCallExpression(node.AsCallExpression()!, false);
            case Kind.PropertyAccessExpression:
            case Kind.ElementAccessExpression:
                if ((node.Flags & NodeFlags.OptionalChain) != 0)
                {
                    return VisitOptionalExpression(node, false, false);
                }

                return Visitor().VisitEachChild(node);
            case Kind.DeleteExpression:
                return VisitDeleteExpression(node.AsDeleteExpression()!);
            default:
                return Visitor().VisitEachChild(node);
        }
    }

    /// <summary>Go: <c>func (ch *optionalChainTransformer) visitCallExpression(node *ast.CallExpression, captureThisArg bool) *ast.Node</c>。</summary>
    private Node? VisitCallExpression(CallExpression node, bool captureThisArg)
    {
        if ((node.Flags & NodeFlags.OptionalChain) != 0)
        {
            // If `node` is an optional chain, then it is the outermost chain of an optional expression.
            return VisitOptionalExpression(node, captureThisArg, false);
        }

        if (IsParenthesizedExpression(node.Expression!))
        {
            var unwrapped = SkipParentheses(node.Expression);
            if ((unwrapped!.Flags & NodeFlags.OptionalChain) != 0)
            {
                // capture thisArg for calls of parenthesized optional chains like `(foo?.bar)()`
                var expression = VisitParenthesizedExpression(node.Expression!.AsParenthesizedExpression()!, true, false);
                var args = Visitor().VisitNodes(node.Arguments);
                if (IsSyntheticReferenceExpression(expression!))
                {
                    var res = Factory().NewFunctionCallCall(
                        expression!.AsSyntheticReferenceExpression()!.Expression!,
                        expression.AsSyntheticReferenceExpression()!.ThisArg!,
                        args!.Nodes!);
                    res.Loc = node.Loc;
                    EmitContext().SetOriginal(res, node);
                    return res;
                }

                return Factory().UpdateCallExpression(node, expression, null /*questionDotToken*/, null /*typeArguments*/, args, node.Flags);
            }
        }

        return Visitor().VisitEachChild(node);
    }

    /// <summary>Go: <c>func (ch *optionalChainTransformer) visitParenthesizedExpression(node *ast.ParenthesizedExpression, captureThisArg bool, isDelete bool) *ast.Node</c>。</summary>
    private Node? VisitParenthesizedExpression(ParenthesizedExpression node, bool captureThisArg, bool isDelete)
    {
        var expr = VisitNonOptionalExpression(node.Expression!, captureThisArg, isDelete);
        if (IsSyntheticReferenceExpression(expr!))
        {
            // `(a.b)` -> { expression `((_a = a).b)`, thisArg: `_a` }
            // `(a[b])` -> { expression `((_a = a)[b])`, thisArg: `_a` }
            var synth = expr!.AsSyntheticReferenceExpression()!;
            var res = Factory().NewSyntheticReferenceExpression(Factory().UpdateParenthesizedExpression(node, synth.Expression), synth.ThisArg);
            EmitContext().SetOriginal(res, node);
            return res;
        }

        return Factory().UpdateParenthesizedExpression(node, expr);
    }

    /// <summary>Go: <c>func (ch *optionalChainTransformer) visitPropertyOrElementAccessExpression(node *ast.Expression, captureThisArg bool, isDelete bool) *ast.Expression</c>。</summary>
    private Node? VisitPropertyOrElementAccessExpression(Node node, bool captureThisArg, bool isDelete)
    {
        if ((node.Flags & NodeFlags.OptionalChain) != 0)
        {
            // If `node` is an optional chain, then it is the outermost chain of an optional expression.
            return VisitOptionalExpression(node, captureThisArg, isDelete);
        }

        var expression = Visitor().VisitNode(node.Expression());
        Debug.Assert(expression is null || !IsSyntheticReferenceExpression(expression));

        Node? thisArg = null;
        if (captureThisArg)
        {
            if (!IsSimpleCopiableExpression(expression!))
            {
                thisArg = Factory().NewTempVariable();
                EmitContext().AddVariableDeclaration(thisArg);
                expression = Factory().NewAssignmentExpression(thisArg, expression!);
            }
            else
            {
                thisArg = expression;
            }
        }

        if (node.Kind == Kind.PropertyAccessExpression)
        {
            var p = node.AsPropertyAccessExpression()!;
            expression = Factory().UpdatePropertyAccessExpression(p, expression, null /*questionDotToken*/, Visitor().VisitNode(p.Name()), p.Flags);
        }
        else
        {
            var p = node.AsElementAccessExpression()!;
            expression = Factory().UpdateElementAccessExpression(p, expression, null, Visitor().VisitNode(p.ArgumentExpression), p.Flags);
        }

        if (thisArg is not null)
        {
            var res = Factory().NewSyntheticReferenceExpression(expression, thisArg);
            EmitContext().SetOriginal(res, node);
            return res;
        }

        return expression;
    }

    /// <summary>Go: <c>func (ch *optionalChainTransformer) visitDeleteExpression(node *ast.DeleteExpression) *ast.Node</c>。</summary>
    private Node? VisitDeleteExpression(DeleteExpression node)
    {
        var unwrapped = SkipParentheses(node.Expression);
        if ((unwrapped!.Flags & NodeFlags.OptionalChain) != 0)
        {
            return VisitNonOptionalExpression(node.Expression!, false, true);
        }

        return Visitor().VisitEachChild(node);
    }

    /// <summary>Go: <c>func (ch *optionalChainTransformer) visitNonOptionalExpression(node *ast.Expression, captureThisArg bool, isDelete bool) *ast.Expression</c>。</summary>
    private Node? VisitNonOptionalExpression(Node node, bool captureThisArg, bool isDelete)
    {
        switch (node.Kind)
        {
            case Kind.ParenthesizedExpression:
                return VisitParenthesizedExpression(node.AsParenthesizedExpression()!, captureThisArg, isDelete);
            case Kind.ElementAccessExpression:
            case Kind.PropertyAccessExpression:
                return VisitPropertyOrElementAccessExpression(node, captureThisArg, isDelete);
            case Kind.CallExpression:
                return VisitCallExpression(node.AsCallExpression()!, captureThisArg);
            default:
                return Visitor().VisitNode(node);
        }
    }

    /// <summary>Go: <c>func (ch *optionalChainTransformer) visitOptionalExpression(node *ast.Node, captureThisArg bool, isDelete bool) *ast.Node</c>。</summary>
    private Node? VisitOptionalExpression(Node node, bool captureThisArg, bool isDelete)
    {
        var r = FlattenChain(node);
        var expression = r._expression;
        var chain = r._chain;
        var left = VisitNonOptionalExpression(SkipPartiallyEmittedExpressions(expression)!, IsCallChain(chain[0]), false);
        Node? leftThisArg = null;
        var capturedLeft = left;
        if (IsSyntheticReferenceExpression(left!))
        {
            leftThisArg = left!.AsSyntheticReferenceExpression()!.ThisArg;
            capturedLeft = left.AsSyntheticReferenceExpression()!.Expression;
        }

        var leftExpression = Factory().RestoreOuterExpressions(expression, capturedLeft!, OuterExpressionKinds.PartiallyEmittedExpressions);
        if (!IsSimpleCopiableExpression(capturedLeft!))
        {
            capturedLeft = Factory().NewTempVariable();
            EmitContext().AddVariableDeclaration(capturedLeft);
            leftExpression = Factory().NewAssignmentExpression(capturedLeft, leftExpression);
        }

        var rightExpression = capturedLeft;
        Node? thisArg = null;

        for (var i = 0; i < chain.Length; i++)
        {
            var segment = chain[i];
            switch (segment.Kind)
            {
                case Kind.ElementAccessExpression:
                case Kind.PropertyAccessExpression:
                    if (i == chain.Length - 1 && captureThisArg)
                    {
                        if (!IsSimpleCopiableExpression(rightExpression!))
                        {
                            thisArg = Factory().NewTempVariable();
                            EmitContext().AddVariableDeclaration(thisArg);
                            rightExpression = Factory().NewAssignmentExpression(thisArg, rightExpression!);
                        }
                        else
                        {
                            thisArg = rightExpression;
                        }
                    }

                    if (segment.Kind == Kind.ElementAccessExpression)
                    {
                        rightExpression = Factory().NewElementAccessExpression(rightExpression!, null, Visitor().VisitNode(segment.AsElementAccessExpression()!.ArgumentExpression), NodeFlags.None);
                    }
                    else
                    {
                        rightExpression = Factory().NewPropertyAccessExpression(rightExpression!, null, Visitor().VisitNode(segment.AsPropertyAccessExpression()!.Name()), NodeFlags.None);
                    }

                    break;
                case Kind.CallExpression:
                    if (i == 0 && leftThisArg is not null)
                    {
                        if (!EmitContext().HasAutoGenerateInfo(leftThisArg))
                        {
                            leftThisArg = leftThisArg.Clone(Factory())!;
                            EmitContext().AddEmitFlags(leftThisArg, EmitFlags.NoComments);
                        }

                        var callThisArg = leftThisArg;
                        if (leftThisArg.Kind == Kind.SuperKeyword)
                        {
                            callThisArg = Factory().NewThisExpression();
                        }

                        rightExpression = Factory().NewFunctionCallCall(rightExpression!, callThisArg, Visitor().VisitNodes(segment.ArgumentList())!.Nodes!);
                    }
                    else
                    {
                        rightExpression = Factory().NewCallExpression(
                            rightExpression,
                            null,
                            null,
                            Visitor().VisitNodes(segment.ArgumentList()),
                            NodeFlags.None);
                    }

                    break;
            }

            EmitContext().SetOriginal(rightExpression!, segment);
        }

        Node? target;
        if (isDelete)
        {
            target = Factory().NewConditionalExpression(
                CreateNotNullCondition(EmitContext(), leftExpression, capturedLeft!, true),
                Factory().NewToken(Kind.QuestionToken),
                Factory().NewTrueExpression(),
                Factory().NewToken(Kind.ColonToken),
                Factory().NewDeleteExpression(rightExpression!));
        }
        else
        {
            target = Factory().NewConditionalExpression(
                CreateNotNullCondition(EmitContext(), leftExpression, capturedLeft!, true),
                Factory().NewToken(Kind.QuestionToken),
                Factory().NewVoidZeroExpression(),
                Factory().NewToken(Kind.ColonToken),
                rightExpression);
        }

        target.Loc = node.Loc;
        if (thisArg is not null)
        {
            target = Factory().NewSyntheticReferenceExpression(target, thisArg);
        }

        EmitContext().SetOriginal(target, node);
        return target;
    }
}

public static partial class EsTransforms
{
    /// <summary>Go: <c>func isNonNullChain(node *ast.Node) bool</c>。</summary>
    internal static bool IsNonNullChain(Node node)
    {
        return IsNonNullExpression(node) && (node.Flags & NodeFlags.OptionalChain) != 0;
    }

    /// <summary>Go: <c>func flattenChain(chain *ast.Node) flattenResult</c>。</summary>
    internal static FlattenResult FlattenChain(Node chain)
    {
        Debug.Assert(!IsNonNullChain(chain));
        var links = new List<Node> { chain };
        while (!IsTaggedTemplateExpression(chain) && chain.QuestionDotToken() is null)
        {
            chain = SkipPartiallyEmittedExpressions(chain.Expression())!;
            Debug.Assert(!IsNonNullChain(chain));
            links.Insert(0, chain);
        }

        return new FlattenResult(chain.Expression(), links.ToArray());
    }

    /// <summary>Go: <c>func isCallChain(node *ast.Node) bool</c>。</summary>
    internal static bool IsCallChain(Node node)
    {
        return IsCallExpression(node) && (node.Flags & NodeFlags.OptionalChain) != 0;
    }

    /// <summary>Go: <c>func newOptionalChainTransformer(opts *transformers.TransformOptions) *transformers.Transformer</c>。</summary>
    internal static Transformer? NewOptionalChainTransformer(TransformOptions opts)
    {
        var tx = new OptionalChainTransformer();
        return tx.NewTransformer(tx.Visit, opts.Context);
    }
}
