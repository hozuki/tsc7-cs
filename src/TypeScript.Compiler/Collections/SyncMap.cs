// ported from: internal/collections/syncmap.go
//
// 与 Go 的偏离（逐条说明）：
//
// 1) Go 的 `_ [0]K; _ [0]V` 两个零长度数组字段是"禁止对这个结构体做 == 比较"的 Go 技巧，C# 的引用类型
//    没有对应物，跳过。
//
// 2) `sync.Map` → `ConcurrentDictionary<K, Slot>`。**关键点：Go 的 sync.Map 存的是 any，可以存 nil**
//    （syncmap_test.go 的 TestSyncMapWithNil 就是 `Store("foo", nil)` 然后 `Load` 要返回 (nil, true)），
//    而 ConcurrentDictionary 不允许 null 值（`TryAdd(key, null)` / 索引器赋值都会抛
//    ArgumentNullException）。所以这里用一层**值类型** Slot 包住 V：Slot 本身永不为 null，
//    "键存在但值是 nil" 因此能被如实表达。用 Slot（而不是把值装箱成 object）是为了不让值类型实参
//    （FileEmitKind、bool、time.Time、time.Duration…）每次读写都装箱。
//
// 3) Load 的两个分支：Go 是
//        val, ok := s.m.Load(key); if !ok || val == nil { return value, ok }; return val.(V), true
//    即"键不存在"和"键存在但存的是 nil 接口"都返回 (V 的零值, ok)。C# 侧 TryGetValue 成功就返回
//    slot.Value（它可能就是 null = V 的零值），失败返回 (default, ok=false)，与 Go 逐分支一致；
//    Go 里为了避开 `val.(V)` 对 nil 接口 panic 的那次判断，在 C# 里由 Slot 结构天然承担。
//    （注意 Go 存的是**带类型的 nil 指针**时接口非 nil，返回的是该 nil 指针 + ok=true；C# 里
//    存 null、返回 null + ok=true，可观察结果相同。）
//
// 4) LoadOrStore：Go 的 sync.Map.LoadOrStore 原子语义 —— 已存在则返回旧值且 loaded=true，否则存入并返回
//    传入的值且 loaded=false。C# 的 ConcurrentDictionary 只有 GetOrAdd/TryAdd，这里用
//    "TryGetValue → TryAdd → 失败则重试" 的循环实现（每个 key 上的原子性与 Go 一致；重试只在竞争时发生）。
//
// 5) Range：Go 的 sync.Map.Range 是弱一致遍历，回调**不持锁**（回调里可以继续 Store/Delete/Range）。
//    ConcurrentDictionary 的枚举同样是弱一致快照、不阻塞写，因此这里直接 foreach 后调用回调，语义相同：
//    遍历期间并发的增删**可能看到也可能看不到**（Go 的文档也是这么写的）。回调返回 false 即停止遍历。
//    回调的 value 类型是 V?（Go 回调的 V 可能是 nil，见第 2 条）。
//
// 6) Size：Go 的实现是 Range 计数，文档明确说是**近似值**；C# 用 ConcurrentDictionary.Count
//    （它会短暂取所有锁，更精确但更贵）。两者都只保证近似/瞬时正确，调用方本就不能依赖精确值。
//
// 7) ToMap 的返回值：Go 是 `map[K]V`；C# 用 `Dictionary<K, V?>`（值可能为 nil，见第 2 条）。
//
// 8) Clone：Go 遍历原表把**原始 any** 存进新表；C# 复制 Slot（值语义），效果相同。Go 返回 *SyncMap
//    （恒非 nil），C# 返回非空引用。
//
// 9) nil 接收者：SyncMap 在 Go 里是结构体（零值可用），C# 里是类但字段在构造函数里初始化，
//    `new SyncMap<K,V>()` 就是 Go 的零值（Go 的 `collections.SyncMap[K,V]{}` 复合字面量）；
//    实例方法不能在 null 引用上分派，Go 里本来也没有 nil 接收者分支。
//
// 10) 泛型约束：ConcurrentDictionary 要求 K 满足 notnull（CS8714），故加 `where K : notnull`
//    （对应 Go 的 `K comparable`）。Go 允许 nil 接口/指针当键（Range 里还有 `if key != nil` 的兜底），
//    C# 侧不支持 —— 见 "unverified"。
using System.Collections.Concurrent;

namespace TypeScript.Collections;

/// <summary>
/// Go 的 <c>SyncMap[K,V]</c>：并发安全的 map（Go 的 <c>sync.Map</c>），zero value 即可用。
/// </summary>
public sealed class SyncMap<K, V> where K : notnull
{
    // Go: `m sync.Map`。见文件头第 2 条：用 Slot 而不是 object 是为了支持 nil 值且不装箱。
    private readonly ConcurrentDictionary<K, Slot> m;

    /// <summary>Go 的零值 <c>SyncMap[K,V]{}</c>；C# 里对应 <c>new SyncMap&lt;K,V&gt;()</c>。</summary>
    public SyncMap()
    {
        m = new ConcurrentDictionary<K, Slot>();
    }

    // sync.Map 里存的是 any（可以存 nil），ConcurrentDictionary 不接受 null 值，故包一层值类型。
    private readonly struct Slot
    {
        public readonly V Value;

        public Slot(V value)
        {
            Value = value;
        }
    }

    /// <summary>Go: <c>Load(key) (value V, ok bool)</c>。键不存在返回 (V 的零值, false)；值本身是 nil 时返回 (nil, true)。</summary>
    public (V? Value, bool Ok) Load(K key)
    {
        if (m.TryGetValue(key, out var slot))
        {
            return (slot.Value, true);
        }

        return (default, false);
    }

    public void Store(K key, V value)
    {
        m[key] = new Slot(value);
    }

    /// <summary>
    /// Go: <c>LoadOrStore(key, value) (actual V, loaded bool)</c>：键已存在时返回旧值且 loaded=true，
    /// 否则存入 value 并返回 (value, false)。
    /// </summary>
    public (V? Value, bool Loaded) LoadOrStore(K key, V value)
    {
        while (true)
        {
            if (m.TryGetValue(key, out var existing))
            {
                return (existing.Value, true);
            }

            if (m.TryAdd(key, new Slot(value)))
            {
                return (value, false);
            }
        }
    }

    public void Delete(K key)
    {
        m.TryRemove(key, out _);
    }

    public void Clear()
    {
        m.Clear();
    }

    /// <summary>
    /// Go: <c>Range(f func(key K, value V) bool)</c>：回调返回 false 停止遍历。弱一致遍历，见文件头第 5 条。
    /// </summary>
    public void Range(Func<K, V?, bool> f)
    {
        foreach (var pair in m)
        {
            if (!f(pair.Key, pair.Value.Value))
            {
                return;
            }
        }
    }

    /// <summary>
    /// Size returns the approximate number of items in the map.
    /// Note that this is not a precise count, as the map may be modified concurrently while this method is running.
    /// </summary>
    public int Size()
    {
        return m.Count;
    }

    public Dictionary<K, V?> ToMap()
    {
        // Go: m := make(map[K]V, s.Size())
        var result = new Dictionary<K, V?>(m.Count);
        foreach (var pair in m)
        {
            result[pair.Key] = pair.Value.Value;
        }

        return result;
    }

    /// <summary>Keys returns an iterator over the keys.（Go: <c>iter.Seq[K]</c>）</summary>
    public IEnumerable<K> Keys()
    {
        foreach (var pair in m)
        {
            yield return pair.Key;
        }
    }

    public SyncMap<K, V> Clone()
    {
        var clone = new SyncMap<K, V>();
        foreach (var pair in m)
        {
            clone.m[pair.Key] = pair.Value;
        }

        return clone;
    }
}
