// ported from: internal/collections/multimap.go
//
// 与 Go 的偏离：
//
// 1) `M map[K][]V` → `public Dictionary<K, List<V>> M`：Go 的导出字段语义保住（调用方可直接读写该字段，
//    `len(s.M)` → `s.M.Count`）。Go 的 `[]V` 切片 → List<V>：`append(values[:i], values[i+1:]...)`
//    并写回 `s.M[key]` 对应 `List<V>.RemoveAt(i)`，改的是同一个 List 实例，语义一致。
// 2) **nil slice 与空 slice 的区别不被抹平**：Go 的 `Get(key)` 返回 `s.M[key]`，键不存在时是 nil slice。
//    C# 用 `List<V>?` 如实表达（键不存在 → null）。注意 Go 的 nil slice 可以直接 range，C# 里
//    `foreach (var v in mm.Get(key))` 会抛 NullReferenceException —— 移植调用点时写
//    `foreach (var v in mm.Get(key) ?? [])`，或先 Has(key) 再取。
// 3) M 的可空性：Go 的零值 MultiMap 的 M 是 nil map（Add 里按需 make）；C# 在构造函数里建成空字典，
//    所以 `new MultiMap<K,V>()` 就是 Go 的零值（Go 的 `collections.MultiMap[K,V]{}` 复合字面量）；
//    `if s.M == nil` 分支等价于"对空字典操作"，不再保留。Clear 复用已分配空间（Go 的 clear(s.M)）。
// 4) Keys()/Values() 在 Go 里是 maps.Keys/maps.Values，迭代顺序是 Go map 的随机顺序；C# 的 Dictionary
//    枚举顺序未定义，调用方本来就不能依赖顺序，语义一致。
// 5) 泛型约束：Dictionary 要求 K 满足 notnull（否则 CS8714，本项目 TreatWarningsAsErrors），故加
//    `where K : notnull`（对应 Go 的 `K comparable`）。V 不加约束：Go 的 `V comparable` 只用于
//    slices.Index 的 `==`，C# 用 EqualityComparer<V>.Default（List<V>.IndexOf）。
// 6) GroupBy 的 `items []V` → `IEnumerable<V>`（Go 切片在 C# 里的自然对应，且递归调用点传的就是切片）。

namespace TypeScript.Collections;

/// <summary>Go 的 <c>MultiMap[K,V]</c>：一个键映射到多个值（Go 的 <c>map[K][]V</c>）。</summary>
public sealed class MultiMap<K, V> where K : notnull
{
    /// <summary>Go 的导出字段 <c>M map[K][]V</c>（见文件头第 1、3 条）。</summary>
    public Dictionary<K, List<V>> M;

    /// <summary>Go 的零值 <c>MultiMap[K,V]{}</c>；C# 里对应 <c>new MultiMap&lt;K,V&gt;()</c>。</summary>
    public MultiMap()
    {
        M = new Dictionary<K, List<V>>();
    }

    // 对应 Go 的 &MultiMap[K,V]{M: make(map[K][]V, hint)}（NewMultiMapWithSizeHint）
    internal MultiMap(int hint)
    {
        M = new Dictionary<K, List<V>>(hint);
    }

    public bool Has(K key)
    {
        return M.ContainsKey(key);
    }

    /// <summary>
    /// Get returns the values stored for the key, or <c>null</c> if the key is absent
    /// （Go 的 <c>s.M[key]</c>，键不存在时是 nil slice；见文件头第 2 条）。
    /// </summary>
    public List<V>? Get(K key)
    {
        return M.TryGetValue(key, out var values) ? values : null;
    }

    public void Add(K key, V value)
    {
        if (!M.TryGetValue(key, out var values))
        {
            values = new List<V>();
            M[key] = values;
        }

        values.Add(value);
    }

    public void Remove(K key, V value)
    {
        if (M.TryGetValue(key, out var values))
        {
            var i = values.IndexOf(value); // Go: slices.Index(values, value)
            if (i >= 0)
            {
                if (values.Count == 1)
                {
                    // Go: delete(s.M, key)（只剩一个值时连键一起删掉）
                    M.Remove(key);
                }
                else
                {
                    // Go: values = append(values[:i], values[i+1:]...); s.M[key] = values
                    values.RemoveAt(i);
                }
            }
        }
    }

    public void RemoveAll(K key)
    {
        M.Remove(key);
    }

    public int Len()
    {
        return M.Count;
    }

    /// <summary>Keys returns an iterator over the keys.（Go: <c>maps.Keys(s.M)</c> → <c>iter.Seq[K]</c>）</summary>
    public IEnumerable<K> Keys()
    {
        return M.Keys;
    }

    /// <summary>Values returns an iterator over the value lists.（Go: <c>maps.Values(s.M)</c> → <c>iter.Seq[[]V]</c>）</summary>
    public IEnumerable<List<V>> Values()
    {
        return M.Values;
    }

    /// <summary>Clear removes everything（Go: <c>clear(s.M)</c>，复用已分配空间）。</summary>
    public void Clear()
    {
        M.Clear();
    }
}

public static partial class Collections
{
    public static MultiMap<K, V> NewMultiMapWithSizeHint<K, V>(int hint) where K : notnull
    {
        return new MultiMap<K, V>(hint);
    }

    public static MultiMap<K, V> GroupBy<K, V>(IEnumerable<V> items, Func<V, K> groupId) where K : notnull
    {
        var m = new MultiMap<K, V>();
        foreach (var item in items)
        {
            m.Add(groupId(item), item);
        }

        return m;
    }
}
