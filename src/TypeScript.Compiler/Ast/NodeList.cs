// ported from: internal/ast/ast.go（NodeList / ModifierList）
//
// Go 的 `ModifierList` 内嵌 `NodeList` → C# 直接继承。Go 里 `Nodes []*Node` 是切片（零拷贝视图），
// C# 用 `Node?[]?`（长期持有的字段，所以不能用 Span，见 docs/architecture.md §3.8）。
using TypeScript.Core;

namespace TypeScript.Ast;

/// <summary>Go 的 <c>type NodeList struct { Loc core.TextRange; Nodes []*Node }</c>。</summary>
public class NodeList
{
    public TextRange Loc;
    public Node[]? Nodes;

    public int Pos() => Loc.Pos();

    public int End() => Loc.End();

    public bool HasTrailingComma()
    {
        var nodes = Nodes;
        if (nodes is null || nodes.Length == 0)
        {
            return false;
        }

        var last = nodes[^1]!;
        return last.End() < End();
    }

    public NodeList Clone(NodeFactoryCoercible f)
    {
        var result = f.AsNodeFactory().NewNodeList(Nodes);
        result.Loc = Loc;
        return result;
    }
}

/// <summary>Go 的 <c>type ModifierList struct { NodeList; ModifierFlags ModifierFlags }</c>。</summary>
public sealed class ModifierList : NodeList
{
    public ModifierFlags ModifierFlags;

    /// <summary>Go: <c>func (list *ModifierList) Clone(f *NodeFactory) *ModifierList</c>（Go 直接复制字段，不走工厂）。</summary>
    public ModifierList Clone(NodeFactory f)
    {
        return new ModifierList
        {
            Loc = Loc,
            Nodes = Nodes,
            ModifierFlags = ModifierFlags,
        };
    }
}
