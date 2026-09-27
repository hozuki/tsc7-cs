// ported from: internal/checker/links.go:1-50（整文件）
//
// 与 Go 的偏离（逐条，均为本仓库已批准的跨 agent 约定）：
// 1) 泛型结构体 `nodeLinkStore[V any]` → `NodeLinkStore<V>`、`symbolArenaLinkStore[V any]` → `SymbolLinkStore<V>`
//    （docs/architecture.md §2：未导出类型按 C# 习惯命名，不必同名；docs/checker-plan.md §2 已指定这两个名字）。
//    两者都是 sealed class：Node/Symbol 的键身份语义与 store 的惰性创建语义都需要引用语义。
// 2) `core.Arena[V]` 不移植（docs/architecture.md §3.2）：Go 的 arena 在这里只是 V 的分配器（`arena.New() *V`）。
//    C# 里“惰性创建一个零值 V 并存回槽位”由 `Core.PagedLinkStore<V>.Get` 直接承担（`V : class, new()`，
//    见 Core/LinkStore.cs 文件头第 1、2 条），因此 `SymbolLinkStore.Get` 里 Go 的
//    `link := s.store.Get(...); if *link == nil { *link = s.arena.New() }` 落成一次 `_store.Get(...)`。
// 3) `ast.GetNodeId(node)` / `ast.GetSymbolId(symbol)` 返回的 NodeId/SymbolId 有到 ulong 的 implicit 转换
//    （Ast/Ids.cs），直接传给 `PagedLinkStore<ulong>` 的键参数，与 Go 的 `uint64(...)` 显式转换等价。
// 4) **页粒度 `Has` 如实复刻**：Go 的 `PagedLinkStore.Has(key)` 是“该键所在的**页**是否分配过”，不是“该键的槽位是否
//    创建过”（Core/LinkStore.cs 里 C# 的 Has/TryGet 与 Go 逐行对应）。这里照抄，不做“修正”：
//    `NodeLinkStore.Has` 直接转调 `_store.Has`；`SymbolLinkStore.Has` 保持 Go 的 `TryGet(symbol) != nil`
//    （Go 那版的 TryGet 会把“页存在但槽位为 nil”也判为 nil，C# 的 `PagedLinkStore.TryGet` 同样返回 null）。
// 5) Go 的 `Get`（两个 store 都是）返回指向槽位的指针，调用点可**写**它；C# 的 `PagedLinkStore.Get` 返回槽位对象
//    （不存在则 `new V()` 并写回），调用点同样可写其字段，语义一致。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using static TypeScript.Ast.Ast;

/// <summary>
/// Go: <c>type nodeLinkStore[V any] struct { store core.PagedLinkStore[V] }</c>（links.go:10-12）。
/// 以 node 引用为键的 links store；值直接存在 store 的页里（Go 注释：适合 sizeof(V) 小的值）。
/// </summary>
internal sealed class NodeLinkStore<V>
    where V : class, new()
{
    private readonly PagedLinkStore<V> _store = new();

    /// <summary>Go: <c>func (s *nodeLinkStore[V]) Get(node *ast.Node) *V</c>（links.go:14-16）。</summary>
    public V Get(Node node)
    {
        return _store.Get(GetNodeId(node));
    }

    /// <summary>Go: <c>func (s *nodeLinkStore[V]) Has(node *ast.Node) bool</c>（links.go:18-20）。页粒度，见文件头 4。</summary>
    public bool Has(Node node)
    {
        return _store.Has(GetNodeId(node));
    }

    /// <summary>Go: <c>func (s *nodeLinkStore[V]) TryGet(node *ast.Node) *V</c>（links.go:22-24）。</summary>
    public V? TryGet(Node node)
    {
        return _store.TryGet(GetNodeId(node));
    }
}

/// <summary>
/// Go: <c>type symbolArenaLinkStore[V any] struct { store core.PagedLinkStore[*V]; arena core.Arena[V] }</c>
/// （links.go:26-29）。以 symbol 引用为键的 links store；值间接存放（Go 用 arena，适合 sizeof(V) 较大的值）。
/// </summary>
internal sealed class SymbolLinkStore<V>
    where V : class, new()
{
    private readonly PagedLinkStore<V> _store = new();

    /// <summary>
    /// Go: <c>func (s *symbolArenaLinkStore[V]) Get(symbol *ast.Symbol) *V</c>（links.go:35-40）。
    /// Go 的 `if *link == nil { *link = s.arena.New() }` 由 `PagedLinkStore.Get` 承担（见文件头 2）。
    /// </summary>
    public V Get(Symbol symbol)
    {
        return _store.Get(GetSymbolId(symbol));
    }

    /// <summary>Go: <c>func (s *symbolArenaLinkStore[V]) Has(symbol *ast.Symbol) bool</c>（links.go:42-44）。</summary>
    public bool Has(Symbol symbol)
    {
        return TryGet(symbol) is not null;
    }

    /// <summary>Go: <c>func (s *symbolArenaLinkStore[V]) TryGet(symbol *ast.Symbol) *V</c>（links.go:46-51）。</summary>
    public V? TryGet(Symbol symbol)
    {
        return _store.TryGet(GetSymbolId(symbol));
    }
}
