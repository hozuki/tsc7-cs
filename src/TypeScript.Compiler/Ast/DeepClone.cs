// ported from: internal/ast/deepclone.go
//
// 与 Go 的差异（C# 语言特性；依据 docs/architecture.md §2、§3.2）：
// - `func (f *NodeFactory) DeepClone*` 落在 partial class `NodeFactory`（与 NodeFactory.cs/Nodes.Generated.cs 拼同一个类）。
// - Go 的 `getDeepCloneVisitor` 用闭包引用稍后才赋值的 `visitor` 变量；C# 先构造 `NodeVisitor`（visit 传 null）再给
//   `Visit` 赋值——lambda 只会在构造完成之后被调用，所以语义相同且不需要 `!`。
// - Go 的指针比较 `visited != node` → `!ReferenceEquals(...)`（AST 节点身份 = 引用，见 docs/ast.md §2）。
// - Go 的 `node.Clone(f)` 可能返回 nil（未覆写 Clone 的节点）；Go 只在 `syntheticLocation` 时解引用它，
//   所以 C# 侧同样只在那一支用 `c!`（nil 时 NRE ≈ Go 的 panic）。`DeepCloneReparse` 里的 `node!` 同理
//   （Go 接下来会解引用 `node.Flags`）。
// - Go 的 `nodes.HasTrailingComma()` + `newList.Nodes[len(...)-1]` → 索引器 `[^1]`。
using TypeScript.Core;
using static TypeScript.Core.Core;

namespace TypeScript.Ast;

public partial class NodeFactory
{
    /// <summary>
    /// Go: <c>func getDeepCloneVisitor(f *NodeFactory, syntheticLocation bool) *NodeVisitor</c>。
    /// Go 的注释：理想情况下它应该缓存在工厂上，这样一个工厂只造一套闭包。
    /// </summary>
    private static NodeVisitor GetDeepCloneVisitor(NodeFactory f, bool syntheticLocation)
    {
        var visitor = new NodeVisitor(
            null,
            f,
            new NodeVisitorHooks
            {
                VisitNodes = (nodes, v) =>
                {
                    if (nodes is null)
                    {
                        return null;
                    }

                    var visited = v.VisitNodes(nodes);
                    var newList = !ReferenceEquals(visited, nodes) ? visited : nodes.Clone(v.Factory);
                    if (syntheticLocation)
                    {
                        newList!.Loc = NewTextRange(-1, -1);
                        if (nodes.HasTrailingComma())
                        {
                            newList.Nodes![^1]!.Loc = NewTextRange(-2, -2);
                        }
                    }

                    return newList;
                },
                VisitModifiers = (nodes, v) =>
                {
                    if (nodes is null)
                    {
                        return null;
                    }

                    var visited = v.VisitModifiers(nodes);
                    var newList = !ReferenceEquals(visited, nodes) ? visited : nodes.Clone(v.Factory);
                    if (syntheticLocation)
                    {
                        newList!.Loc = NewTextRange(-1, -1);
                        if (nodes.HasTrailingComma())
                        {
                            newList.Nodes![^1]!.Loc = NewTextRange(-2, -2);
                        }
                    }

                    return newList;
                },
            });

        visitor.Visit = node =>
        {
            var visited = visitor.VisitEachChild(node);
            if (!ReferenceEquals(visited, node))
            {
                if (syntheticLocation)
                {
                    visited!.Loc = NewTextRange(-1, -1);
                }

                return visited;
            }

            // 强制克隆叶子节点，随后的 update 调用会把新节点/数组向上级联。
            var c = node.Clone(f);
            // 在 strada 里 `factory.cloneNode` 是动态的、不会为任何"特例"克隆位置；而 corsa 的 Node.Clone 一律走
            // Update，因而默认会带上位置。深克隆是为了把节点复制到另一个文件，所以这里显式把所有克隆节点的位置
            // 置为合成区间。
            if (syntheticLocation)
            {
                c!.Loc = NewTextRange(-1, -1);
            }

            return c;
        };
        return visitor;
    }

    /// <summary>Go: <c>func (f *NodeFactory) DeepCloneNode(node *Node) *Node</c>。</summary>
    public Node? DeepCloneNode(Node? node) => GetDeepCloneVisitor(this, true /*syntheticLocation*/).VisitNode(node);

    /// <summary>Go: <c>func (f *NodeFactory) DeepCloneReparse(node *Node) *Node</c>。</summary>
    public Node? DeepCloneReparse(Node? node)
    {
        if (node is not null)
        {
            node = GetDeepCloneVisitor(this, false /*syntheticLocation*/).VisitNode(node);
            Ast.SetParentInChildren(node!);
            node!.Flags |= NodeFlags.Reparsed;
        }

        return node;
    }

    /// <summary>Go: <c>func (f *NodeFactory) DeepCloneReparseModifiers(modifiers *ModifierList) *ModifierList</c>。</summary>
    public ModifierList? DeepCloneReparseModifiers(ModifierList? modifiers) =>
        GetDeepCloneVisitor(this, false /*syntheticLocation*/).VisitModifiers(modifiers);
}
