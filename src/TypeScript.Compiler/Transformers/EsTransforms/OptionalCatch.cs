// ported from: internal/transformers/estransforms/optionalcatch.go（37 行，全部：optionalCatchTransformer +
//               newOptionalCatchTransformer）
//
// 与 Go 的差异（逐条）：
// 1) Go 的 `type optionalCatchTransformer struct { transformers.Transformer }`（嵌入）→ C# 的
//    `internal sealed class OptionalCatchTransformer : Transformer`（继承，见 Transformers/Transformer.cs 的文件头契约）。
// 2) Go 的包级工厂 `newOptionalCatchTransformer` → `EsTransforms.NewOptionalCatchTransformer`（docs/architecture.md §2），
//    返回 `Transformer?`（Go 的 `*transformers.Transformer` 可为 nil；消费者 `Transformers.Chain` 会跳过 nil）。
// 3) Go 的 `func (ch *optionalCatchTransformer) visit` → `internal Node? Visit(Node node)`（工厂是**独立类**的成员，
//    所以方法取 internal 而不是 private）。
using TypeScript.Ast;
using TypeScript.Printer;
using static TypeScript.Ast.Ast;
using static TypeScript.Transformers.EsTransforms.EsTransforms;

namespace TypeScript.Transformers.EsTransforms;

internal sealed class OptionalCatchTransformer : Transformer
{
    /// <summary>Go: <c>func (ch *optionalCatchTransformer) visit(node *ast.Node) *ast.Node</c>。</summary>
    internal Node? Visit(Node node)
    {
        if ((node.SubtreeFacts & SubtreeFacts.ContainsMissingCatchClauseVariable) == 0)
        {
            return node;
        }

        switch (node.Kind)
        {
            case Kind.CatchClause:
                return VisitCatchClause(node.AsCatchClause()!);
            default:
                return Visitor().VisitEachChild(node);
        }
    }

    /// <summary>Go: <c>func (ch *optionalCatchTransformer) visitCatchClause(node *ast.CatchClause) *ast.Node</c>。</summary>
    private Node? VisitCatchClause(CatchClause node)
    {
        if (node.VariableDeclaration is null)
        {
            return Factory().NewCatchClause(
                Factory().NewVariableDeclaration(Factory().NewTempVariable(), null, null, null),
                Visitor().Visit!(node.Block!));
        }

        return Visitor().VisitEachChild(node);
    }
}

public static partial class EsTransforms
{
    /// <summary>Go: <c>func newOptionalCatchTransformer(opts *transformers.TransformOptions) *transformers.Transformer</c>。</summary>
    internal static Transformer? NewOptionalCatchTransformer(TransformOptions opts)
    {
        var tx = new OptionalCatchTransformer();
        return tx.NewTransformer(tx.Visit, opts.Context);
    }
}
