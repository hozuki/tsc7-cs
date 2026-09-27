// ported from: internal/project/refcountcache.go:1-137
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) Go 未导出结构体 `refCountCacheEntry[V]` → C# `internal sealed class RefCountCacheEntry<V>`
//    （类型名按 C# 习惯命名）。字段：`mu` → `_mu`（只本类用 → private；测试要读 refCount，故 refCount → `internal _refCount`）、
//    `value` → `internal _value`。
// 2) `sync.Mutex` → **`System.Threading.Lock`**（仓内先例 Compiler/CheckerPool.cs）；Go 的
//    `defer entry.mu.Unlock()` / 跨调用的"返回时持锁"语义用显式 `_mu.Enter()`/`_mu.Exit()` + `try/finally` 复刻。
//    Go 的 `entry.mu.Lock()` 在 `loadOrStoreNewLockedEntry` 里是**跨函数边界持锁**，不能用 `lock` 语句。
// 3) `collections.SyncMap[K, *refCountCacheEntry[V]]` → `TypeScript.Collections.SyncMap<K, RefCountCacheEntry<V>>`
//    （已落地；`Load` → `(V? Value, bool Ok)`、`LoadOrStore` → `(V? Value, bool Loaded)`、`Delete`、`Store`）。
//    `K comparable` → C# 的 `where K : notnull`（ConcurrentDictionary 的要求，SyncMap.cs 文件头第 10 条既定处理）。
//    Go 未导出字段 `entries` → `internal SyncMap<…> _entries`（测试要直接读它）；Go 导出字段 `Options` 保留 public。
// 4) `NewRefCountCache(options, parse)` 是**包级构造函数** → 按 M8Project 广播 #2 挂成它构造的类型的静态成员：
//    `RefCountCache<K,V,H>.NewRefCountCache(...)`；另提供等价的 public 构造函数（Go 的"结构体字面量 + parse 字段"
//    在 C# 里必须由构造函数接收 parse，否则子类无法复用）。
// 5) `func(K, AcquireArgs) V` → `Func<K, H, V>`；`AcquireArgs any` → 泛型参数 `H`（Go 的 `any` 在 C# 里不能当泛型实参
//    的类型参数用 `object`，因为 `any` 是"任意类型"而非"object"——`ContentMappedParseCache` 用 `struct{}` 实例化，
//    所以必须保留泛型参数）。
// 6) `AcquireOrError(identity K, produce func() (V, error)) (V, error)` → `V AcquireOrError(K identity, Func<V> produce)`
//    （docs §3.2：`(T, error)` → 正常返回值 + 抛异常；调用方 `errors.AsType` → `catch`）。
//    Go 的"produce 失败则不缓存"分支落成 `try/catch`：`_refCount = 0` + `_entries.Delete(identity)` 后重新抛出。
// 7) `panic("cache entry not found")` → `Debug.Fail(...)`（抛 DebugFailureException，docs §3.2）。
// 8) 递归的 `loadOrStoreNewLockedEntry` 逐字保留（Go 在"读到的条目已被删掉"时递归重试；递归发生在**刚创建、
//    尚未共享**的新条目持锁期间，不会自死锁——`Lock` 可重入也不影响，因为那两种情况用的是不同对象）。
namespace TypeScript.Project;

using System;
using TypeScript.Collections;

/// <summary>Go: <c>type RefCountCacheOptions struct</c>（refcountcache.go:15-19）。</summary>
public sealed class RefCountCacheOptions
{
    /// <summary>Go: <c>DisableDeletion bool</c>。禁止条目被移除（测试用）。</summary>
    public bool DisableDeletion;
}

/// <summary>Go: <c>type refCountCacheEntry[V any] struct</c>（refcountcache.go:9-13）。</summary>
internal sealed class RefCountCacheEntry<V>
{
    // Go: `mu sync.Mutex`
    internal readonly Lock _mu = new();

    // Go: `value V`
    internal V _value = default!;

    // Go: `refCount int`
    internal int _refCount;
}

/// <summary>
/// Go: <c>type RefCountCache[K comparable, V any, AcquireArgs any] struct</c>（refcountcache.go:21-26）。
/// 按 identity 缓存值并计数引用；引用归零时删除条目（除非 <see cref="RefCountCacheOptions.DisableDeletion"/>）。
/// </summary>
public class RefCountCache<K, V, H> where K : notnull
{
    /// <summary>Go: <c>Options RefCountCacheOptions</c>。</summary>
    public RefCountCacheOptions Options;

    /// <summary>Go: <c>entries collections.SyncMap[K, *refCountCacheEntry[V]]</c>。</summary>
    internal SyncMap<K, RefCountCacheEntry<V>> _entries = new();

    /// <summary>Go: <c>parse func(K, AcquireArgs) V</c>。</summary>
    private readonly Func<K, H, V> _parse;

    /// <summary>Go: <c>func NewRefCountCache[K, V, AcquireArgs](options RefCountCacheOptions, parse func(K, AcquireArgs) V) *RefCountCache[K, V, AcquireArgs]</c>（refcountcache.go:28-35）。</summary>
    public static RefCountCache<K, V, H> NewRefCountCache(RefCountCacheOptions options, Func<K, H, V> parse)
    {
        return new RefCountCache<K, V, H>(options, parse);
    }

    /// <summary>Go 里由结构体字面量完成的部分（见文件头第 4 条）。</summary>
    public RefCountCache(RefCountCacheOptions options, Func<K, H, V> parse)
    {
        Options = options;
        _parse = parse;
    }

    /// <summary>
    /// Go: <c>func (c *RefCountCache[K, V, AcquireArgs]) Acquire(identity K, acquireArgs AcquireArgs) V</c>
    /// （refcountcache.go:43-52）。命中则返回缓存值（引用计数已自增）；未命中则调用 parse 产生值并以引用计数 1 存入。
    /// </summary>
    public V Acquire(K identity, H acquireArgs)
    {
        var (entry, loaded) = LoadOrStoreNewLockedEntry(identity);
        try
        {
            if (!loaded)
            {
                // 新条目 —— 解析值。
                entry._value = _parse(identity, acquireArgs);
                return entry._value;
            }

            return entry._value;
        }
        finally
        {
            entry._mu.Exit();
        }
    }

    /// <summary>Go: <c>func (c *RefCountCache[K, V, AcquireArgs]) Has(identity K) bool</c>（refcountcache.go:54-57）。</summary>
    public bool Has(K identity)
    {
        return _entries.Load(identity).Ok;
    }

    /// <summary>
    /// Go: <c>func (c *RefCountCache[K, V, AcquireArgs]) AcquireOrError(identity K, produce func() (V, error)) (V, error)</c>
    /// （refcountcache.go:59-78）。已存在则直接返回（引用计数已自增）；否则在**新条目的锁内**调用 produce，
    /// 失败时撤销试探性条目并抛出（只有成功的结果会被缓存）。
    /// </summary>
    public V AcquireOrError(K identity, Func<V> produce)
    {
        var (entry, loaded) = LoadOrStoreNewLockedEntry(identity);
        try
        {
            if (loaded)
            {
                return entry._value;
            }

            V value;
            try
            {
                value = produce();
            }
            catch
            {
                // 撤销试探性条目，失败结果不进缓存。
                entry._refCount = 0;
                _entries.Delete(identity);
                throw;
            }

            entry._value = value;
            return value;
        }
        finally
        {
            entry._mu.Exit();
        }
    }

    /// <summary>
    /// Go: <c>func (c *RefCountCache[K, V, AcquireArgs]) Ref(identity K)</c>（refcountcache.go:80-96）。
    /// 为已存在条目自增引用计数；条目不存在时是内部错误。
    /// </summary>
    public void Ref(K identity)
    {
        var (entry, ok) = _entries.Load(identity);
        if (!ok)
        {
            // Go: panic("cache entry not found")
            Debug.Fail("cache entry not found");
            return;
        }

        entry!._mu.Enter();
        try
        {
            if (entry._refCount <= 0 && !Options.DisableDeletion)
            {
                // 我们抢锁期间条目已被删除。
                var (newEntry, _) = LoadOrStoreNewLockedEntry(identity);
                try
                {
                    newEntry._value = entry._value;
                    return;
                }
                finally
                {
                    newEntry._mu.Exit();
                }
            }

            entry._refCount++;
        }
        finally
        {
            entry._mu.Exit();
        }
    }

    /// <summary>
    /// Go: <c>func (c *RefCountCache[K, V, AcquireArgs]) Deref(identity K)</c>（refcountcache.go:98-110）。
    /// 引用计数减到 0 时删除条目（除非 DisableDeletion）；条目不存在时什么都不做。
    /// </summary>
    public void Deref(K identity)
    {
        var (entry, ok) = _entries.Load(identity);
        if (!ok)
        {
            return;
        }

        entry!._mu.Enter();
        try
        {
            entry._refCount--;
            if (entry._refCount <= 0 && !Options.DisableDeletion)
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
    /// Go: <c>func (c *RefCountCache[K, V, AcquireArgs]) loadOrStoreNewLockedEntry(key K) (*refCountCacheEntry[V], bool)</c>
    /// （refcountcache.go:112-136）。返回的条目**处于已加锁状态**且引用计数已自增（新条目则初始化为 1）。
    /// </summary>
    private (RefCountCacheEntry<V> Entry, bool Loaded) LoadOrStoreNewLockedEntry(K key)
    {
        var entry = new RefCountCacheEntry<V> { _refCount = 1 };
        entry._mu.Enter();
        var (existing, loaded) = _entries.LoadOrStore(key, entry);
        if (loaded)
        {
            entry._mu.Exit();
            existing!._mu.Enter();
            if (existing._refCount <= 0 && !Options.DisableDeletion)
            {
                // 我们抢锁期间该条目已被删除。
                existing._mu.Exit();
                return LoadOrStoreNewLockedEntry(key);
            }

            existing._refCount++;
            return (existing, true);
        }

        return (entry, false);
    }
}
