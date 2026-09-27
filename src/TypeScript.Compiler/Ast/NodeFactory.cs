// ported from: internal/ast/ast.go（NodeFactory / NodeFactoryHooks / NodeFactoryCoercible + 包级 newNode/updateNode/cloneNode）
//
// 与 Go 的差异（C# 语言特性；另见 docs/ast.md §2/§3.2）：
// - Go 的 `core.Arena[T]` 槽位（每个节点一次分配 + 稳定指针）：C# 的 AST 节点是 class，对象引用本身就是
//   稳定的身份载体，所以 **没有 arena 字段**，`data := f.xxxArena.New()` → `new Xxx()`。
//   （Go 调用点里的 `f.nodeListArena.New()`/`f.modifierListArena.New()` 同理 → `new NodeList()`。）
// - Go 的 `func(*Node)` 内联函数类型 → C# 的 `Action<Node>`/`Action<Node, Node>`（省掉只为钩子发明
//   一个委托类型）。钩子字段在 Go 里是导出字段 → C# public 字段。
// - Go 的未导出字段/方法按 C# 习惯命名：`hooks` → `_hooks`、`nodeCount` → `_nodeCount`、
//   `textCount` → `_textCount`、`newNode` → `NewNode`（internal，同名公开形态见 docs/ast.md §2）。
// - Span 优先（docs/architecture.md §3.8）：`NewModifierList`/`ModifiersToFlags` 走 `ReadOnlySpan<Node?>`，
//   不复制数组。
using TypeScript.Core;
using static TypeScript.Core.Core;

namespace TypeScript.Ast;

/// <summary>Go 的 <c>type NodeFactoryCoercible interface { AsNodeFactory() *NodeFactory }</c>。</summary>
public interface NodeFactoryCoercible
{
    NodeFactory AsNodeFactory();
}

/// <summary>Go 的 <c>type NodeFactoryHooks struct</c>。</summary>
public sealed class NodeFactoryHooks
{
    /// <summary>Hooks the creation of a node.</summary>
    public Action<Node>? OnCreate;

    /// <summary>Hooks the updating of a node.</summary>
    public Action<Node, Node>? OnUpdate;

    /// <summary>Hooks the cloning of a node.</summary>
    public Action<Node, Node>? OnClone;
}

public partial class NodeFactory : NodeFactoryCoercible
{
    internal NodeFactoryHooks _hooks = new();
    internal int _nodeCount;
    internal int _textCount;

    /// <summary>Go: <c>func (f *NodeFactory) newNode(kind Kind, data nodeData) *Node</c>。</summary>
    internal Node NewNode(Kind kind, Node node)
    {
        _nodeCount++;
        return Ast.NewNode(kind, node, _hooks);
    }

    public int NodeCount() => _nodeCount;

    public int TextCount() => _textCount;

    public NodeFactory AsNodeFactory() => this;

    /// <summary>Go: <c>func (f *NodeFactory) NewNodeList(nodes []*Node) *NodeList</c>。</summary>
    public NodeList NewNodeList(Node[]? nodes)
    {
        return new NodeList
        {
            Loc = UndefinedTextRange(),
            Nodes = nodes,
        };
    }

    /// <summary>Go: <c>func (f *NodeFactory) NewModifierList(nodes []*Node) *ModifierList</c>。</summary>
    public ModifierList NewModifierList(Node[]? nodes)
    {
        return new ModifierList
        {
            Loc = UndefinedTextRange(),
            Nodes = nodes,
            ModifierFlags = Ast.ModifiersToFlags(nodes.AsSpan()),
        };
    }

    /// <summary>Go: <c>func (f *NodeFactory) NewModifier(kind Kind) *Node</c>（ast.go 手写，委托给生成的 NewToken）。</summary>
    public Node NewModifier(Kind kind) => NewToken(kind);

    /// <summary>
    /// Go: <c>func (f *NodeFactory) ReleaseArenas()</c>（ast.go:3183-3189）。C# 没有 arena（节点是 class），
    /// 所以只重置计数器、保留 hooks（与 Go 的重置结果一致：hooks/textCount/nodeCount 保留，其余清零）。
    /// </summary>
    public void ReleaseArenas()
    {
        _nodeCount = 0;
        _textCount = 0;
    }
}
