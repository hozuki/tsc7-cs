// ported from: internal/transformers/estransforms/logicalassignment.go（113 行，全部：logicalAssignmentTransformer +
//               visitBinaryExpression、newLogicalAssignmentTransformer）
//
// 与 Go 的差异（逐条）：
// 1) Go 的 `type logicalAssignmentTransformer struct { transformers.Transformer }`（嵌入）→ C# 的
//    `internal sealed class LogicalAssignmentTransformer : Transformer`（继承，见 Transformers/Transformer.cs 契约）。
// 2) Go 的包级工厂 `newLogicalAssignmentTransformer` → `EsTransforms.NewLogicalAssignmentTransformer`（§2）。
// 3) Go 的 `var nonAssignmentOperator ast.Kind`（零值 = KindUnknown）→ C# 显式初始化为 `Kind.Unknown`
//    （Go 的零值就是枚举 0；Go 里三个 case 之外的 default 直接 return，不会读到未赋值的值）。
// 4) Go 的 `left.Expression()`/`left.AsElementAccessExpression().ArgumentExpression`（可能为 nil、Go 会 panic）
//    → C# 用 `!` 表达同一不变量；`IsSimpleCopiableExpression` 的参数在 C# 侧是非可空 `Node`。
// 5) `ast.SkipParentheses(...)` → `Ast.SkipParentheses(...)`（返回 `Node?`，与 Go 的 *Expression 一致）。
using TypeScript.Ast;
using static TypeScript.Ast.Ast;
using static TypeScript.Transformers.EsTransforms.EsTransforms;
using static TypeScript.Transformers.Transformers;

namespace TypeScript.Transformers.EsTransforms;

internal sealed class LogicalAssignmentTransformer : Transformer
{
    /// <summary>Go: <c>func (ch *logicalAssignmentTransformer) visit(node *ast.Node) *ast.Node</c>。</summary>
    internal Node? Visit(Node node)
    {
        if ((node.SubtreeFacts & SubtreeFacts.ContainsLogicalAssignments) == 0)
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

    /// <summary>Go: <c>func (ch *logicalAssignmentTransformer) visitBinaryExpression(node *ast.BinaryExpression) *ast.Node</c>。</summary>
    private Node? VisitBinaryExpression(BinaryExpression node)
    {
        var nonAssignmentOperator = Kind.Unknown;
        switch (node.OperatorToken!.Kind)
        {
            case Kind.BarBarEqualsToken:
                nonAssignmentOperator = Kind.BarBarToken;
                break;
            case Kind.AmpersandAmpersandEqualsToken:
                nonAssignmentOperator = Kind.AmpersandAmpersandToken;
                break;
            case Kind.QuestionQuestionEqualsToken:
                nonAssignmentOperator = Kind.QuestionQuestionToken;
                break;
            default:
                return Visitor().VisitEachChild(node);
        }

        var left = SkipParentheses(Visitor().VisitNode(node.Left));
        var assignmentTarget = left;
        var right = SkipParentheses(Visitor().VisitNode(node.Right));

        if (IsAccessExpression(left))
        {
            var propertyAccessTargetSimpleCopiable = IsSimpleCopiableExpression(left!.Expression()!);
            var propertyAccessTarget = left.Expression();
            var propertyAccessTargetAssignment = left.Expression();
            if (!propertyAccessTargetSimpleCopiable)
            {
                propertyAccessTarget = Factory().NewTempVariable();
                EmitContext().AddVariableDeclaration(propertyAccessTarget);
                propertyAccessTargetAssignment = Factory().NewAssignmentExpression(
                    propertyAccessTarget,
                    left.Expression()!);
            }

            if (IsPropertyAccessExpression(left))
            {
                assignmentTarget = Factory().NewPropertyAccessExpression(
                    propertyAccessTarget,
                    null,
                    left.Name(),
                    NodeFlags.None);
                left = Factory().NewPropertyAccessExpression(
                    propertyAccessTargetAssignment,
                    null,
                    left.Name(),
                    NodeFlags.None);
            }
            else
            {
                var elementAccessArgumentSimpleCopiable = IsSimpleCopiableExpression(left.AsElementAccessExpression()!.ArgumentExpression!);
                var elementAccessArgument = left.AsElementAccessExpression()!.ArgumentExpression;
                var argumentExpr = elementAccessArgument;
                if (!elementAccessArgumentSimpleCopiable)
                {
                    elementAccessArgument = Factory().NewTempVariable();
                    EmitContext().AddVariableDeclaration(elementAccessArgument);
                    argumentExpr = Factory().NewAssignmentExpression(
                        elementAccessArgument,
                        left.AsElementAccessExpression()!.ArgumentExpression!);
                }

                assignmentTarget = Factory().NewElementAccessExpression(
                    propertyAccessTarget!,
                    null,
                    elementAccessArgument,
                    NodeFlags.None);
                left = Factory().NewElementAccessExpression(
                    propertyAccessTargetAssignment!,
                    null,
                    argumentExpr,
                    NodeFlags.None);
            }
        }

        return Factory().NewBinaryExpression(
            null,
            left,
            null,
            Factory().NewToken(nonAssignmentOperator),
            Factory().NewParenthesizedExpression(
                Factory().NewAssignmentExpression(
                    assignmentTarget!,
                    right!)));
    }
}

public static partial class EsTransforms
{
    /// <summary>Go: <c>func newLogicalAssignmentTransformer(opts *transformers.TransformOptions) *transformers.Transformer</c>。</summary>
    internal static Transformer? NewLogicalAssignmentTransformer(TransformOptions opts)
    {
        var tx = new LogicalAssignmentTransformer();
        return tx.NewTransformer(tx.Visit, opts.Context);
    }
}
