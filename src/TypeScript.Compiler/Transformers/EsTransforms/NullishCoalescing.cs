// ported from: internal/transformers/estransforms/nullishcoalescing.go（49 行，全部：nullishCoalescingTransformer +
//               newNullishCoalescingTransformer）
//
// 与 Go 的差异（逐条）：
// 1) Go 的 `type nullishCoalescingTransformer struct { transformers.Transformer }`（嵌入）→ C# 的
//    `internal sealed class NullishCoalescingTransformer : Transformer`（继承，见 Transformers/Transformer.cs 文件头契约）。
// 2) Go 的包级工厂 `newNullishCoalescingTransformer` → `EsTransforms.NewNullishCoalescingTransformer`（§2）。
// 3) Go 的 `func (ch *nullishCoalescingTransformer) visit` → `internal Node? Visit(Node node)`（工厂在独立类里，
//    所以取 internal 而不是 private）。
// 4) `node.OperatorToken.Kind`（Go 的字段访问）→ C# `node.OperatorToken!.Kind`（字段是 `Node?`，Go 里同位置会 nil 解引用）。
using TypeScript.Ast;
using static TypeScript.Ast.Ast;
using static TypeScript.Transformers.EsTransforms.EsTransforms;
using static TypeScript.Transformers.Transformers;

namespace TypeScript.Transformers.EsTransforms;

internal sealed class NullishCoalescingTransformer : Transformer
{
    /// <summary>Go: <c>func (ch *nullishCoalescingTransformer) visit(node *ast.Node) *ast.Node</c>。</summary>
    internal Node? Visit(Node node)
    {
        if ((node.SubtreeFacts & SubtreeFacts.ContainsNullishCoalescing) == 0)
        {
            return node;
        }

        switch (node.Kind)
        {
            case Kind.BinaryExpression:
                return VisitBinaryExpression(node.AsBinaryExpression()!);
            default:
                return Visitor().VisitEachChild(node);
        }
    }

    /// <summary>Go: <c>func (ch *nullishCoalescingTransformer) visitBinaryExpression(node *ast.BinaryExpression) *ast.Node</c>。</summary>
    private Node? VisitBinaryExpression(BinaryExpression node)
    {
        switch (node.OperatorToken!.Kind)
        {
            case Kind.QuestionQuestionToken:
            {
                var left = Visitor().VisitNode(node.Left);
                var right = left;
                if (!IsSimpleCopiableExpression(left!))
                {
                    right = Factory().NewTempVariable();
                    EmitContext().AddVariableDeclaration(right);
                    left = Factory().NewAssignmentExpression(right, left!);
                }

                return Factory().NewConditionalExpression(
                    CreateNotNullCondition(EmitContext(), left!, right!, false),
                    Factory().NewToken(Kind.QuestionToken),
                    right,
                    Factory().NewToken(Kind.ColonToken),
                    Visitor().VisitNode(node.Right));
            }

            default:
                return Visitor().VisitEachChild(node);
        }
    }
}

public static partial class EsTransforms
{
    /// <summary>Go: <c>func newNullishCoalescingTransformer(opts *transformers.TransformOptions) *transformers.Transformer</c>。</summary>
    internal static Transformer? NewNullishCoalescingTransformer(TransformOptions opts)
    {
        var tx = new NullishCoalescingTransformer();
        return tx.NewTransformer(tx.Visit, opts.Context);
    }
}
