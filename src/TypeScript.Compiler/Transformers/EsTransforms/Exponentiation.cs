// ported from: internal/transformers/estransforms/exponentiation.go（90 行，全部：exponentiationTransformer +
//               visitBinaryExpression、visitExponentiationAssignmentExpression、visitExponentiationExpression、
//               newExponentiationTransformer）
//
// 与 Go 的差异（逐条）：
// 1) Go 的 `type exponentiationTransformer struct { transformers.Transformer }`（嵌入）→ C# 的
//    `internal sealed class ExponentiationTransformer : Transformer`（继承，见 Transformers/Transformer.cs 契约）。
// 2) Go 的包级工厂 `newExponentiationTransformer` → `EsTransforms.NewExponentiationTransformer`（§2）。
// 3) Go 的 `visit`/`visitBinaryExpression` → `internal Node? Visit` / `private Node? VisitBinaryExpression`
//    （Visit 由独立类的工厂取方法组；其余方法只用在本类内）。
// 4) Go 的 `left`/`right`/`value`/`target` 是 `*ast.Node`（`target`/`value` 声明为 `*ast.Node` 但只在两个分支里赋值，
//    与 `visitExponentiationExpression` 的 `result` 同一形态）→ C# 用 `Node?` + 与 Go 相同的赋值点；
//    Go 在这些位置写 `left.Expression().Loc`（左值可能为 nil 时会 panic）→ C# 的 `!` 表示同一不变量。
using TypeScript.Ast;
using static TypeScript.Ast.Ast;
using static TypeScript.Transformers.EsTransforms.EsTransforms;

namespace TypeScript.Transformers.EsTransforms;

internal sealed class ExponentiationTransformer : Transformer
{
    /// <summary>Go: <c>func (ch *exponentiationTransformer) visit(node *ast.Node) *ast.Node</c>。</summary>
    internal Node? Visit(Node node)
    {
        if ((node.SubtreeFacts & SubtreeFacts.ContainsExponentiationOperator) == 0)
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

    /// <summary>Go: <c>func (ch *exponentiationTransformer) visitBinaryExpression(node *ast.BinaryExpression) *ast.Node</c>。</summary>
    private Node? VisitBinaryExpression(BinaryExpression node)
    {
        switch (node.OperatorToken!.Kind)
        {
            case Kind.AsteriskAsteriskEqualsToken:
                return VisitExponentiationAssignmentExpression(node);
            case Kind.AsteriskAsteriskToken:
                return VisitExponentiationExpression(node);
        }

        return Visitor().VisitEachChild(node);
    }

    /// <summary>Go: <c>func (ch *exponentiationTransformer) visitExponentiationAssignmentExpression(node *ast.BinaryExpression) *ast.Node</c>。</summary>
    private Node? VisitExponentiationAssignmentExpression(BinaryExpression node)
    {
        Node? target;
        Node? value;
        var left = Visitor().VisitNode(node.Left);
        var right = Visitor().VisitNode(node.Right);
        if (IsElementAccessExpression(left!))
        {
            // Transforms `a[x] **= b` into `(_a = a)[_x = x] = Math.pow(_a[_x], b)`
            var expressionTemp = Factory().NewTempVariable();
            EmitContext().AddVariableDeclaration(expressionTemp);
            var argumentExpressionTemp = Factory().NewTempVariable();
            EmitContext().AddVariableDeclaration(argumentExpressionTemp);

            var objExpr = Factory().NewAssignmentExpression(expressionTemp, left!.Expression()!);
            objExpr.Loc = left.Expression()!.Loc;
            var accessExpr = Factory().NewAssignmentExpression(argumentExpressionTemp, left.AsElementAccessExpression()!.ArgumentExpression!);
            accessExpr.Loc = left.AsElementAccessExpression()!.ArgumentExpression!.Loc;

            target = Factory().NewElementAccessExpression(objExpr, null, accessExpr, NodeFlags.None);

            value = Factory().NewElementAccessExpression(expressionTemp, null, argumentExpressionTemp, NodeFlags.None);
            value.Loc = left.Loc;
        }
        else if (IsPropertyAccessExpression(left!))
        {
            // Transforms `a.x **= b` into `(_a = a).x = Math.pow(_a.x, b)`
            var expressionTemp = Factory().NewTempVariable();
            EmitContext().AddVariableDeclaration(expressionTemp);
            var assignment = Factory().NewAssignmentExpression(expressionTemp, left!.Expression()!);
            assignment.Loc = left.Expression()!.Loc;
            target = Factory().NewPropertyAccessExpression(assignment, null, left.Name(), NodeFlags.None);
            target.Loc = left.Loc;

            value = Factory().NewPropertyAccessExpression(expressionTemp, null, left.Name(), NodeFlags.None);
            value.Loc = left.Loc;
        }
        else
        {
            // Transforms `a **= b` into `a = Math.pow(a, b)`
            target = left;
            value = left;
        }

        var rhs = Factory().NewGlobalMethodCall("Math", "pow", [value!, right!]);
        rhs.Loc = node.Loc;
        var result = Factory().NewAssignmentExpression(target!, rhs);
        result.Loc = node.Loc;
        return result;
    }

    /// <summary>Go: <c>func (ch *exponentiationTransformer) visitExponentiationExpression(node *ast.BinaryExpression) *ast.Node</c>。</summary>
    private Node? VisitExponentiationExpression(BinaryExpression node)
    {
        var left = Visitor().VisitNode(node.Left);
        var right = Visitor().VisitNode(node.Right);
        var result = Factory().NewGlobalMethodCall("Math", "pow", [left!, right!]);
        result.Loc = node.Loc;
        return result;
    }
}

public static partial class EsTransforms
{
    /// <summary>Go: <c>func newExponentiationTransformer(opts *transformers.TransformOptions) *transformers.Transformer</c>。</summary>
    internal static Transformer? NewExponentiationTransformer(TransformOptions opts)
    {
        var tx = new ExponentiationTransformer();
        return tx.NewTransformer(tx.Visit, opts.Context);
    }
}
