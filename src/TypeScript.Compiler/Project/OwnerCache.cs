// ported from: internal/project/ownercache.go:1-107
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) Go 未导出结构体 `ownerCacheEntry[V]` → C# `internal sealed class OwnerCacheEntry<V>`。字段：
//    `mu` → `_mu`（private；`Lock` 可重入）、`value` → `internal _value`、`owners map[uint64]struct{}` →
//    `internal HashSet<ulong> _owners`（`len(entry.owners)` → `_owners.Count`）。
// 2) `sync.Mutex` → **`System.Threading.Lock`**（仓内先例 Compiler/CheckerPool.cs）；跨函数边界持锁的
//    `loadOrStoreLockedEntry` 用显式 `Enter()`/`Exit()`，其余用 `try/finally` 复刻 `defer Unlock`。
// 3) `collections.SyncMap[K, *ownerCacheEntry[V]]` → `TypeScript.Collections.SyncMap<K, OwnerCacheEntry<V>>`；
//    `K comparable` → `where K : notnull`（SyncMap.cs 文件头第 10 条）。未导出字段 `entries`/`isExpired`/`parse`
//    → `internal _entries`（测试直接读）/`private _isExpired`/`private _parse`。
// 4) `NewOwnerCache(parse, isExpired)` 是包级构造函数 → 按 M8Project 广播 #2 挂成 `OwnerCache<K,V,LoadArgs>.NewOwnerCache(...)`；
//    另提供 public 构造函数（`ExtendedConfigCache` 派生类必须能调 base——Go 的结构体字面量在 C# 里就是构造函数）。
// 5) 泛型参数名：Go 的 `LoadArgs any` → `H`（同 RefCountCache.cs 文件头第 5 条）。
// 6) `isExpired func(K, V, LoadArgs) bool` 可为 nil（Go 的函数值零值）→ `Func<K, V, H, bool>?`；调用点判空。
// 7) `panic("OwnerCache.AddOwner: entry not found")` / `panic("OwnerCache.AddOwner: entry has no owners")`
//    → `Debug.Fail(...)`（抛 DebugFailureException，docs §3.2）。
// 8) Go 的 `loadOrStoreLockedEntry` 返回"已加锁"的条目；该函数里的递归重试与 RefCountCache 同理（新条目尚未共享）。
namespace TypeScript.Project;

using System;
using System.Collections.Generic;
using TypeScript.Collections;

/// <summary>Go: <c>type ownerCacheEntry[V any] struct</c>（ownercache.go:9-13）。</summary>
internal sealed class OwnerCacheEntry<V>
{
    // Go: `mu sync.Mutex`
    internal readonly Lock _mu = new();

    // Go: `value V`
    internal V _value = default!;

    // Go: `owners map[uint64]struct{}`
    internal HashSet<ulong> _owners = [];
}

/// <summary>
/// Go: <c>type OwnerCache[K comparable, V any, LoadArgs any] struct</c>（ownercache.go:15-29）。
/// 与 <see cref="RefCountCache{K,V,H}"/> 类似，但每个条目记录**所有者集合**而不是计数。
/// </summary>
public class OwnerCache<K, V, H> where K : notnull
{
    /// <summary>Go: <c>entries collections.SyncMap[K, *ownerCacheEntry[V]]</c>。</summary>
    internal SyncMap<K, OwnerCacheEntry<V>> _entries = new();

    /// <summary>Go: <c>isExpired func(K, V, LoadArgs) bool</c>（nil = 永不过期）。</summary>
    private readonly Func<K, V, H, bool>? _isExpired;

    /// <summary>Go: <c>parse func(K, LoadArgs) V</c>。</summary>
    private readonly Func<K, H, V> _parse;

    /// <summary>Go: <c>func NewOwnerCache[K, V, LoadArgs](parse func(K, LoadArgs) V, isExpired func(K, V, LoadArgs) bool) *OwnerCache[K, V, LoadArgs]</c>（ownercache.go:31-38）。</summary>
    public static OwnerCache<K, V, H> NewOwnerCache(Func<K, H, V> parse, Func<K, V, H, bool>? isExpired)
    {
        return new OwnerCache<K, V, H>(parse, isExpired);
    }

    /// <summary>Go 里由结构体字面量完成的部分（见文件头第 4 条）。</summary>
    public OwnerCache(Func<K, H, V> parse, Func<K, V, H, bool>? isExpired)
    {
        _parse = parse;
        _isExpired = isExpired;
    }

    /// <summary>
    /// Go: <c>func (c *OwnerCache[K, V, LoadArgs]) LoadAndAcquire(identity K, owner uint64, loadArgs LoadArgs) V</c>
    /// （ownercache.go:40-48）。条目不存在或已过期时重新 parse，然后把 owner 记入所有者集合。
    /// </summary>
    public V LoadAndAcquire(K identity, ulong owner, H loadArgs)
    {
        var (entry, loaded) = LoadOrStoreLockedEntry(identity);
        try
        {
            if (!loaded || (_isExpired is not null && _isExpired(identity, entry._value, loadArgs)))
            {
                entry._value = _parse(identity, loadArgs);
            }

            entry._owners.Add(owner);
            return entry._value;
        }
        finally
        {
            entry._mu.Exit();
        }
    }

    /// <summary>
    /// Go: <c>func (c *OwnerCache[K, V, LoadArgs]) Acquire(identity K, owner uint64, value V)</c>（ownercache.go:50-58）。
    /// 条目不存在时以给定值建条目（已存在则保留原值），然后把 owner 记入所有者集合。
    /// </summary>
    public void Acquire(K identity, ulong owner, V value)
    {
        var (entry, loaded) = LoadOrStoreLockedEntry(identity);
        try
        {
            if (!loaded)
            {
                entry._value = value;
            }

            entry._owners.Add(owner);
        }
        finally
        {
            entry._mu.Exit();
        }
    }

    /// <summary>
    /// Go: <c>func (c *OwnerCache[K, V, LoadArgs]) AddOwner(identity K, owner uint64)</c>（ownercache.go:60-72）。
    /// 给已存在的存活条目追加所有者；条目必须存在且至少有一个当前所有者。
    /// </summary>
    public void AddOwner(K identity, ulong owner)
    {
        var (entry, ok) = _entries.Load(identity);
        if (!ok)
        {
            // Go: panic("OwnerCache.AddOwner: entry not found")
            Debug.Fail("OwnerCache.AddOwner: entry not found");
            return;
        }

        entry!._mu.Enter();
        try
        {
            if (entry._owners.Count == 0)
            {
                // Go: panic("OwnerCache.AddOwner: entry has no owners")
                Debug.Fail("OwnerCache.AddOwner: entry has no owners");
            }

            entry._owners.Add(owner);
        }
        finally
        {
            entry._mu.Exit();
        }
    }

    /// <summary>Go: <c>func (c *OwnerCache[K, V, LoadArgs]) Has(identity K) bool</c>（ownercache.go:74-77）。</summary>
    public bool Has(K identity)
    {
        return _entries.Load(identity).Ok;
    }

    /// <summary>
    /// Go: <c>func (c *OwnerCache[K, V, LoadArgs]) Release(identity K, owner uint64)</c>（ownercache.go:79-92）。
    /// 移除一个所有者；所有者集合空时删除条目。
    /// </summary>
    public void Release(K identity, ulong owner)
    {
        var (entry, ok) = _entries.Load(identity);
        if (!ok)
        {
            return;
        }

        entry!._mu.Enter();
        try
        {
            entry._owners.Remove(owner);
            if (entry._owners.Count == 0)
            {
                _entries.Delete(identity);
            }
        }
        finally
        {
            entry._mu.Exit();
        }
    }

    /// <summary>
    /// Go: <c>func (c *OwnerCache[K, V, LoadArgs]) loadOrStoreLockedEntry(key K) (*ownerCacheEntry[V], bool)</c>
    /// （ownercache.go:94-106）。返回的条目**处于已加锁状态**；读到"所有者已被清空的条目"时递归重试。
    /// </summary>
    private (OwnerCacheEntry<V> Entry, bool Loaded) LoadOrStoreLockedEntry(K key)
    {
        var entry = new OwnerCacheEntry<V>();
        entry._mu.Enter();
        var (existing, loaded) = _entries.LoadOrStore(key, entry);
        if (loaded)
        {
            entry._mu.Exit();
            existing!._mu.Enter();
            if (existing._owners.Count == 0)
            {
                existing._mu.Exit();
                return LoadOrStoreLockedEntry(key);
            }

            return (existing, true);
        }

        return (entry, false);
    }
}
