// ported from: internal/transformers/estransforms/classthis.go（28 行，全部：isClassThisAssignmentBlock）
//
// 与 Go 的差异（逐条）：
// 1) Go 的包级函数 → `public static partial class EsTransforms` 的 `internal static` 成员（docs/architecture.md §2）；
//    namedevaluation.go/classfields.go 都是**独立类**，所以取 `internal`。
// 2) Go 的 `a == b`（*Node 指针比较）→ C# `ReferenceEquals(a, b)`（Node 未重载 `==`，显式写引用相等与
//    `Ast/Node.cs` 的先例一致）。
// 3) `len(body.Statements.Nodes) == 1`：Go 的 nil 切片长度是 0 → C# 写 `?? 0`，避免把 Go 的"nil → 0"变成 NRE。
using TypeScript.Ast;
using TypeScript.Printer;
using static TypeScript.Ast.Ast;

namespace TypeScript.Transformers.EsTransforms;

public static partial class EsTransforms
{
    /// <summary>
    /// Gets whether a node is a `static {}` block containing only a single assignment of the static `this` to the `_classThis`
    /// (or similar) variable stored in the `classthis` property of the block's `EmitNode`.
    /// </summary>
    internal static bool IsClassThisAssignmentBlock(EmitContext emitContext, Node node)
    {
        if (IsClassStaticBlockDeclaration(node))
        {
            var n = node.AsClassStaticBlockDeclaration()!;
            var body = n.Body!.AsBlock()!;
            if ((body.Statements?.Nodes?.Length ?? 0) == 1)
            {
                var statement = body.Statements!.Nodes![0];
                if (IsExpressionStatement(statement))
                {
                    var expression = statement.Expression();
                    if (IsAssignmentExpression(expression, true /*excludeCompoundAssignment*/))
                    {
                        var binary = expression!.AsBinaryExpression()!;
                        return IsIdentifier(binary.Left!) &&
                            ReferenceEquals(emitContext.ClassThis(node), binary.Left) &&
                            binary.Right!.Kind == Kind.ThisKeyword;
                    }
                }
            }
        }

        return false;
    }
}
