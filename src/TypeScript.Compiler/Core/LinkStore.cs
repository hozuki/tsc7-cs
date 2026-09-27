// ported from: internal/core/linkstore.go
//
// 偏离 Go 的地方（Main 已批准的跨 agent 约定，见 docs/architecture.md 的命名/接口约定）：
// 1) Go 的 `LinkStore[K,V]` / `PagedLinkStore[V]` 用 `Arena[V]` 分配 V 并返回 `*V`：TSGO 里 V 通常是结构体
//    （紧凑存储、地址稳定）。C# 侧不用 Arena：V 约束为 `class, new()`，`new V()` 就是 Go 的 "arena 里分配一个
//    零值 V"，`null` 就是 Go 的 `*V == nil`。**性能偏离**：每个槽位一次堆分配、页数组是引用数组（Go 是紧凑
//    值数组）。Go 的 `arena` 字段因此不再需要（Go 只把它当分配器），换算成 C# 的 GC 分配。
// 2) Go 的 `Get` 返回指向零值槽位的指针（页不存在则先建页）；C# 的“零值槽位”对应新建实例，所以 `Get`
//    在槽位为 null 时 `new V()`；`TryGet` 不创建任何东西，槽位未创建时返回 null。
// 3) Go 的 `map[K]*V` → `Dictionary<K, V>`（K 需要 `notnull` 约束，Dictionary 的要求；TSGO 的键
//    （*ast.Node、*ast.Symbol、tspath.Path、string…）都满足）。
// 4) Go 的 `PagedLinkStore` 用 `[]*[pageSize]V` 切片当页表 → C# 用 `List<V?[]?>`（页表长度按需增长，
//    对应 Go 的 `slices.Grow(...)[:pageIndex+1]`）；`map[uint64]*[pageSize]V` → `Dictionary<ulong, V?[]>`。
//    包级常量 pageShift/pageSize/pageMask/maxPageCount 放在 `Core` 里（internal，与原样的小写名一致）。
namespace TypeScript.Core;

// Links store

public sealed class LinkStore<K, V>
    where K : notnull
    where V : class, new()
{
    private Dictionary<K, V>? _entries;

    public V Get(K key)
    {
        if (_entries is null)
        {
            _entries = [];
        }
        if (_entries.TryGetValue(key, out var value))
        {
            return value;
        }
        value = new V();
        _entries[key] = value;
        return value;
    }

    public bool Has(K key)
    {
        return _entries is not null && _entries.ContainsKey(key);
    }

    public V? TryGet(K key)
    {
        if (_entries is not null && _entries.TryGetValue(key, out var value))
        {
            return value;
        }
        return null;
    }
}

public static partial class Core
{
    internal const int _pageShift = 8;
    internal const int _pageSize = 1 << _pageShift;
    internal const int _pageMask = _pageSize - 1;
    internal const ulong _maxPageCount = 65536;
}

// Implements a sparse-array-like structure for storing elements keyed by dense uint64 keys. Elements are
// stored in fixed-size pages of 256 entries and an index of pages is maintained in an array for lower valued
// page indices and a map for higher valued page indices.
public sealed class PagedLinkStore<V>
    where V : class, new()
{
    private Dictionary<ulong, V?[]>? _pageMap; // Page map for page indices above maxPageCount
    private readonly List<V?[]?> _pageList = []; // Page table for page indices below maxPageCount

    public V Get(ulong key)
    {
        V?[] page;
        var pageIndex = key >> Core._pageShift;
        if (pageIndex < Core._maxPageCount)
        {
            if ((int)pageIndex >= _pageList.Count)
            {
                // Grow the length of the list to pageIndex+1
                while (_pageList.Count <= (int)pageIndex)
                {
                    _pageList.Add(null);
                }
            }
            var listPage = _pageList[(int)pageIndex];
            if (listPage is null)
            {
                listPage = new V?[Core._pageSize];
                _pageList[(int)pageIndex] = listPage;
            }
            page = listPage;
        }
        else
        {
            _pageMap ??= [];
            if (!_pageMap.TryGetValue(pageIndex, out var mapPage))
            {
                mapPage = new V?[Core._pageSize];
                _pageMap[pageIndex] = mapPage;
            }
            page = mapPage;
        }
        var index = (int)(key & Core._pageMask);
        var value = page[index];
        if (value is null)
        {
            value = new V();
            page[index] = value;
        }
        return value;
    }

    // Go: `func (s *PagedLinkStore[V]) Has(key uint64) bool { return s.TryGet(key) != nil }`，而 Go 的 TryGet
    // 在**页已分配**时就返回 `&page[slot]`（非 nil，哪怕槽位是零值）⇒ Go 的 Has 是**页粒度**的：同一页里
    // 任意键被访问过，该页所有键的 Has 都为 true。这里如实复刻页粒度（C# 里"页是否存在"看页表项是否为 null），
    // 与 Go 逐行对应；调用点（checker 的 nodeLinkStore.Has）读到的零值 link 与 Go 的零值槽位一致。
    public bool Has(ulong key)
    {
        var pageIndex = key >> Core._pageShift;
        return pageIndex < Core._maxPageCount
            ? (int)pageIndex < _pageList.Count && _pageList[(int)pageIndex] is not null
            : _pageMap is not null && _pageMap.TryGetValue(pageIndex, out var page) && page is not null;
    }

    // 注意与 Go 的一处偏离：Go 的 TryGet 在页存在时返回**零值槽位的指针**（非 nil），C# 的槽位在
    // `Get` 之前是 null，所以这里返回 `V?`（null = 该槽位尚未创建）。调用点若沿用 Go 的
    // `if link := TryGet(k); link != nil { …读字段… }`，在 C# 里改为 `var link = TryGet(k); if (link is not null) { … }`
    // 语义一致（Go 那段对零值槽位读到的也是零值字段）；若 Go 的代码**写**这个指针，必须换成 `Get(k)`。
    public V? TryGet(ulong key)
    {
        V?[]? page = null;
        var pageIndex = key >> Core._pageShift;
        if (pageIndex < Core._maxPageCount)
        {
            if ((int)pageIndex < _pageList.Count)
            {
                page = _pageList[(int)pageIndex];
            }
        }
        else if (_pageMap is not null)
        {
            _pageMap.TryGetValue(pageIndex, out page);
        }
        if (page is not null)
        {
            return page[(int)(key & Core._pageMask)];
        }
        return null;
    }
}
