// ported from: internal/execute/build/parseCache.go
//
// 与 Go 的偏离（docs/architecture.md §2/§3）：
// 1) `sync.Mutex` → `lock`/`Monitor`；`collections.SyncMap` → C# `SyncMap`。
// 2) Go 的泛型约束 `V comparable` 与 `*new(V)` 零值判断 → `EqualityComparer<V>.Default.Equals(value, default!)`
//    （本包的所有实例化里 V 都是引用类型，`default` 即 `null`，与 Go 的 `*new(V)` 等价的"未设置"判定）。
// 3) **不导出类型名**：Go 的 `parseCacheEntry` → `ParseCacheEntry`、`parseCache` → `ParseCache`（§2）。
// 4) 未导出方法 → internal + PascalCase（`loadOrStore` → `LoadOrStore`）。
namespace TypeScript.Execute.Build;

using TypeScript.Collections;

/// <summary>Go: <c>type parseCacheEntry[V comparable] struct</c>（parseCache.go:9-12）。</summary>
internal sealed class ParseCacheEntry<V>
{
    internal V Value = default!;

    internal readonly object Mu = new();
}

/// <summary>Go: <c>type parseCache[K comparable, V comparable] struct</c>（parseCache.go:14-16）。</summary>
internal sealed class ParseCache<K, V>
    where K : notnull
{
    private SyncMap<K, ParseCacheEntry<V>> _entries = new();

    /// <summary>Go: <c>func (c *parseCache[K, V]) loadOrStore(key K, parse func(K) V, allowZero bool) V</c>（parseCache.go:18-32）。</summary>
    internal V LoadOrStore(K key, Func<K, V> parse, bool allowZero)
    {
        var newEntry = new ParseCacheEntry<V>();
        Monitor.Enter(newEntry.Mu);
        // Go 的 `defer newEntry.mu.Unlock()` 在 defer 时**取定接收者**（方法值），所以即使下面把变量重新指向
        // 已缓存的 entry（`newEntry = entry`），最终解锁的仍是这里新建的那个 entry —— 用局部变量锁定同样的对象。
        var lockedEntry = newEntry;
        try
        {
            var (entry, loaded) = _entries.LoadOrStore(key, newEntry);
            if (loaded)
            {
                Monitor.Enter(entry!.Mu);
                try
                {
                    if (allowZero || !EqualityComparer<V>.Default.Equals(entry.Value, default!))
                    {
                        return entry.Value;
                    }
                    lockedEntry = entry;
                }
                finally
                {
                    Monitor.Exit(entry.Mu);
                }
            }
            lockedEntry.Value = parse(key);
            return lockedEntry.Value;
        }
        finally
        {
            Monitor.Exit(newEntry.Mu);
        }
    }

    /// <summary>Go: <c>func (c *parseCache[K, V]) store(key K, value V)</c>（parseCache.go:34-36）。</summary>
    internal void Store(K key, V value) => _entries.Store(key, new ParseCacheEntry<V> { Value = value });

    /// <summary>Go: <c>func (c *parseCache[K, V]) delete(key K)</c>（parseCache.go:38-40）。</summary>
    internal void Delete(K key) => _entries.Delete(key);

    /// <summary>Go: <c>func (c *parseCache[K, V]) reset()</c>（parseCache.go:42-44）。</summary>
    internal void Reset() => _entries = new SyncMap<K, ParseCacheEntry<V>>();
}
