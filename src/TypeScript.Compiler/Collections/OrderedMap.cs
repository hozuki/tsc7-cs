// ported from: internal/collections/ordered_map.go
//
// 与 Go 的偏离（逐条说明；除标注"性能偏离"外都是语义等价，只为对齐 C# 的语言特性）：
//
// 1) noCopy：Go 的 OrderedMap 内嵌 `noCopy`（零尺寸结构体 + Lock/Unlock 空方法），只为 go vet 的
//    -copylocks 检查"这个结构体用过之后不许再按值拷贝"。C# 的引用类型本来就按引用传递，没有对应物，
//    该字段跳过（Go ordered_map.go:13-25）。
//
// 2) keys：Go 是 `keys []K`（切片）。Delete 对"删首/删尾"用 `m.keys = m.keys[1:]` / `m.keys[:end]`
//    做 O(1) 重新切片、把腾出的槽位置为 K 的零值，只有删中间才搬移。C# 用 List<K>：没有切片视图，
//    三种情况统一成 `List.RemoveAt`——**性能偏离**（删首是 O(n) 搬移，Go 是 O(1)），但可观察语义一致：
//    List.RemoveAt 会把搬移后空出的槽位置为 default，等价于 Go 的 `m.keys[i] = zero`（不保留已删元素
//    的引用）；顺序、内容、Size/Keys/Values/Entries 的结果都相同。
//
// 3) keys/mp 的可空性：Go 的零值 OrderedMap 两个字段都是 nil，Set 里按需 make。C# 在构造函数里建成
//    空容器，所以 `new OrderedMap<K,V>()` 就是 Go 的零值 `OrderedMap[K,V]{}`（Go 的
//    `var m collections.OrderedMap[K,V]` 对应 `var m = new OrderedMap<K,V>()`）；Go 里
//    `if m.mp == nil` 这类分支等价于"对空容器操作"，不再保留。Clear 复用已分配空间（容器不重建），
//    与 Go 的 `clear + 重新切片` 一致。
//
// 4) 值语义：Go 的 Get/GetOrZero/EntryAt/Delete/Values/Entries 在键不存在时返回 V 的零值；C# 的
//    default(V) 对引用类型是 null，所以这些位置的类型用 `V?` 如实表达"可能是 nil"（对值类型实参
//    `V?` 不产生 Nullable<T>，与 V 相同 —— 未约束类型参数上的 `T?` 只是可空注解，不改变实例化后的类型）。
//
// 5) iter.Seq[T] → IEnumerable<T>、iter.Seq2[K,V] → IEnumerable<(K Key, V Value)>，用 yield return 实现
//    （C# 的迭代器同样是拉取式，`yield break`/返回到调用方等价于 Go 的 `yield` 返回 false）。
//    Go 注释里"用下标 for 循环而不是 range，以便枚举到迭代期间新增的键"这条语义照搬。
//
// 6) Clone/clone：Go 的 Clone 返回 *OrderedMap（nil 接收者返回 nil），内部 clone() 返回结构体值。
//    C# 里 clone() 那层值返回没有意义，合并成一个私有构造 + 两个容器的拷贝构造。
//
// 7) nil 接收者：Go 的 Size/Keys/Values/Entries/Clone 对 nil 接收者分别返回 0 / 空迭代器 / nil。
//    C# 的实例方法不能在 null 引用上分派（调用即抛 NullReferenceException），这些分支不可达，不再保留；
//    移植调用点时写 `m?.Size() ?? 0`、`m?.Keys()`、`m?.Clone()`。
//
// 8) 泛型约束：Go 的 `K comparable` → C# 的 Dictionary<K,V> 要求 K 满足 `notnull`（否则 CS8714，
//    本项目 TreatWarningsAsErrors 会失败），故加 `where K : notnull`。TSGO 用到的 key（tspath.Path、
//    string、SymbolId/TypeId 等值类型、*ast.Node 等引用类型）都满足；代价是 C# 里不能用 `string?`
//    这类可空引用类型当 key（Go 允许 nil 指针/接口当 key），见"unverified"。V 不加约束，判等用
//    EqualityComparer<V>.Default，对应 Go 的 `==`。
//
// JSON（Go ordered_map.go:157-268）：MarshalJSONTo / UnmarshalJSONFrom 已实现。Go 的
// resolveKeyName 用 reflect 判键类型，AOT 下不允许反射（docs §3.6），改成显式分派（见方法处的注释）。
// `Json.X` 在 `TypeScript.*` 命名空间里会被同名命名空间遮蔽（docs §2），所以按约定用
// `using static` + 裸名，与 Go 的 `json.MarshalEncode`/`json.BeginObject` 一一对应。
using System.Globalization;
using TypeScript.Json;
using static TypeScript.Json.Json;

namespace TypeScript.Collections;

/// <summary>Go 的 <c>MapEntry[K,V]</c>：一个键值对（字段在 Go 里是导出且可变的，这里保持一致）。</summary>
public struct MapEntry<K, V>
{
    public K Key;
    public V Value;

    public MapEntry(K key, V value)
    {
        Key = key;
        Value = value;
    }
}

/// <summary>
/// Go 的 <c>OrderedMap[K,V]</c>：插入有序的 map（用 keys 维护插入顺序 + mp 做 O(1) 查找）。
/// </summary>
public sealed class OrderedMap<K, V> : IJsonMarshaler, IJsonUnmarshaler where K : notnull
{
    private readonly List<K> _keys;
    private readonly Dictionary<K, V> _mp;

    /// <summary>Go 的零值 <c>OrderedMap[K,V]{}</c>；C# 里对应 <c>new OrderedMap&lt;K,V&gt;()</c>。</summary>
    public OrderedMap()
    {
        _keys = new List<K>();
        _mp = new Dictionary<K, V>();
    }

    // 对应 Go 的 newMapWithSizeHint[K,V](hint)（NewOrderedMapWithSizeHint / OrderedSet 的构造）
    internal OrderedMap(int hint)
    {
        _keys = new List<K>(hint);
        _mp = new Dictionary<K, V>(hint);
    }

    // 对应 Go 的 clone() 的返回值（keys: slices.Clone, mp: maps.Clone）
    internal OrderedMap(List<K> keys, Dictionary<K, V> mp)
    {
        this._keys = keys;
        this._mp = mp;
    }

    /// <summary>Set sets a key-value pair in the map.（新键追加到末尾；已有键只改值、位置不变）</summary>
    public void Set(K key, V value)
    {
        if (!_mp.ContainsKey(key))
        {
            _keys.Add(key);
        }

        _mp[key] = value;
    }

    /// <summary>Get retrieves a value from the map.</summary>
    public (V? Value, bool Ok) Get(K key)
    {
        if (_mp.TryGetValue(key, out var value))
        {
            return (value, true);
        }

        return (default, false);
    }

    /// <summary>
    /// GetOrZero retrieves a value from the map, or returns the zero value of the value type if the key
    /// is not present.
    /// </summary>
    public V? GetOrZero(K key)
    {
        // Go: return m.mp[key]（键不存在时是 V 的零值）
        return _mp.TryGetValue(key, out var value) ? value : default;
    }

    /// <summary>EntryAt retrieves the key-value pair at the specified index.</summary>
    public (K? Key, V? Value, bool Ok) EntryAt(int index)
    {
        if (index < 0 || index >= _keys.Count)
        {
            // Go: var zero K; var zeroV V; return zero, zeroV, false
            return (default, default, false);
        }

        var key = _keys[index];
        // 用索引器而不是 TryGetValue：keys 与 mp 始终同步，key 一定存在（Go 的 `m.mp[key]` 也是这样），
        // 而且索引器的返回类型是 V 而不是 V?（不需要在实现里判空再转一次）。
        return (key, _mp[key], true);
    }

    /// <summary>Has returns true if the map contains the key.</summary>
    public bool Has(K key)
    {
        return _mp.ContainsKey(key);
    }

    /// <summary>Delete removes a key-value pair from the map.</summary>
    public (V? Value, bool Ok) Delete(K key)
    {
        if (!_mp.TryGetValue(key, out var value))
        {
            // Go: var zero V; return zero, false
            return (default, false);
        }

        _mp.Remove(key);

        // Go: i == 0 时 `m.keys[0] = zero; m.keys = m.keys[1:]`，i == len-1 时 `m.keys[end] = zero;
        //     m.keys = m.keys[:end]`，其余 `slices.Delete`。见文件头第 2 条：C# 统一为 RemoveAt。
        _keys.RemoveAt(_keys.IndexOf(key));
        return (value, true);
    }

    /// <summary>Keys returns an iterator over the keys in the map.（Go: <c>iter.Seq[K]</c>）</summary>
    public IEnumerable<K> Keys()
    {
        // 用下标 for 循环（而不是 foreach），这样迭代期间新增的键也会被枚举到；Go 的注释同此。
        for (var i = 0; i < _keys.Count; i++)
        {
            yield return _keys[i];
        }
    }

    /// <summary>Values returns an iterator over the values in the map.（Go: <c>iter.Seq[V]</c>）</summary>
    public IEnumerable<V?> Values()
    {
        // 同样用下标 for 循环，以便枚举到迭代期间新增的键的值。值走索引器（keys/mp 同步，键一定存在）。
        for (var i = 0; i < _keys.Count; i++)
        {
            yield return _mp[_keys[i]];
        }
    }

    /// <summary>
    /// Entries returns an iterator over the key-value pairs in the map.（Go: <c>iter.Seq2[K,V]</c>）
    /// </summary>
    public IEnumerable<(K Key, V? Value)> Entries()
    {
        for (var i = 0; i < _keys.Count; i++)
        {
            var key = _keys[i]; // 值走索引器，理由同 Values()
            yield return (key, _mp[key]);
        }
    }

    /// <summary>
    /// Clear removes all key-value pairs from the map. The space allocated for the map will be reused.
    /// </summary>
    public void Clear()
    {
        // Go: clear(m.keys); m.keys = m.keys[:0]; clear(m.mp) —— 不重建容器，已分配空间复用
        _keys.Clear();
        _mp.Clear();
    }

    /// <summary>Size returns the number of key-value pairs in the map.</summary>
    public int Size()
    {
        return _keys.Count;
    }

    /// <summary>Clone returns a shallow copy of the map.（键值本身不复制）</summary>
    public OrderedMap<K, V> Clone()
    {
        // Go: OrderedMap{keys: slices.Clone(m.keys), mp: maps.Clone(m.mp)}
        return new OrderedMap<K, V>(new List<K>(_keys), new Dictionary<K, V>(_mp));
    }

    // Go: var _ json.MarshalerTo = (*OrderedMap[string, string])(nil)
    /// <summary>Go 的 <c>MarshalJSONTo</c>：按插入顺序把键值写成 JSON 对象。</summary>
    public void MarshalJSONTo(Encoder enc)
    {
        enc.WriteToken(BeginObject);
        foreach (var k in _keys)
        {
            // Go: 先 resolveKeyName(k) 把键转成字符串，再 MarshalEncode 键与值
            MarshalEncode(enc, JsonKeyName(k));
            MarshalEncode(enc, _mp[k]);
        }

        enc.WriteToken(EndObject);
    }

    // Go: var _ json.UnmarshalerFrom = (*OrderedMap[string, string])(nil)
    /// <summary>
    /// Go 的 <c>UnmarshalJSONFrom</c>：JSON <c>null</c> 按 <c>Unmarshaler</c> 约定当 no-op；
    /// 非对象值报错（错误文本与 Go 一致，见 ordered_map.go:275-277）。
    /// </summary>
    public void UnmarshalJSONFrom(Decoder dec)
    {
        var token = dec.ReadToken();
        if (token.Kind == JsonKind.Null)
        {
            return;
        }

        if (token.Kind != JsonKind.BeginObject)
        {
            throw new JsonException("cannot unmarshal non-object JSON value into Map");
        }

        while (dec.PeekKind() != JsonKind.EndObject)
        {
            // Go: json.UnmarshalDecode(dec, &key) / (dec, &value) —— 对象里的成员名本身就是一个字符串
            // token，所以这样一对一对读。
            var key = dec.Decode<K>();
            if (key is null)
            {
                throw new JsonException("json: cannot unmarshal null into a map key");
            }

            var value = dec.Decode<V>();
            Set(key, value!);
        }

        dec.ReadToken(); // 消费 ObjectEnd
    }

    // Go 的 resolveKeyName（ordered_map.go:158-174）用 reflect 判 Kind、strconv 转数字、encoding.TextMarshaler
    // 取名字。AOT 下不允许反射（docs §3.6），改成对可用键型的显式分派：字符串键原样（Go 的 reflect.String），
    // 整数键按 strconv.FormatInt/FormatUint 的十进制语义。Go 对不支持的类型 panic，这里抛 JsonException，
    // 语义一致。TSGO 只对 OrderedMap[string, ...] 断言了 MarshalerTo，所以字符串是实际用到的唯一键型。
    // Go 支持的 encoding.TextMarshaler 键在移植时用字符串包装表达（用到时再补显式接口）。
    private static string JsonKeyName(K key) => key switch
    {
        string s => s,
        sbyte v => v.ToString(CultureInfo.InvariantCulture),
        byte v => v.ToString(CultureInfo.InvariantCulture),
        short v => v.ToString(CultureInfo.InvariantCulture),
        ushort v => v.ToString(CultureInfo.InvariantCulture),
        int v => v.ToString(CultureInfo.InvariantCulture),
        uint v => v.ToString(CultureInfo.InvariantCulture),
        long v => v.ToString(CultureInfo.InvariantCulture),
        ulong v => v.ToString(CultureInfo.InvariantCulture),
        nint v => v.ToString(CultureInfo.InvariantCulture),
        nuint v => v.ToString(CultureInfo.InvariantCulture),
        _ => throw new JsonException($"unexpected map key type {typeof(K)}"),
    };
}

public static partial class Collections
{
    /// <summary>Creates a new OrderedMap with a hint for the number of elements it will contain.</summary>
    public static OrderedMap<K, V> NewOrderedMapWithSizeHint<K, V>(int hint) where K : notnull
    {
        return new OrderedMap<K, V>(hint);
    }

    // Go 的 newMapWithSizeHint[K,V]（未导出、包内使用：OrderedSet 的构造）→ C# internal
    internal static OrderedMap<K, V> NewMapWithSizeHint<K, V>(int hint) where K : notnull
    {
        return new OrderedMap<K, V>(hint);
    }

    public static OrderedMap<K, V> NewOrderedMapFromList<K, V>(MapEntry<K, V>[] items) where K : notnull
    {
        var mp = NewOrderedMapWithSizeHint<K, V>(items.Length);
        foreach (var item in items)
        {
            mp.Set(item.Key, item.Value);
        }

        return mp;
    }

    /// <summary>Go 的 <c>DiffOrderedMaps</c>：值用 <c>==</c>（C# 用 EqualityComparer&lt;V&gt;.Default）。</summary>
    public static void DiffOrderedMaps<K, V>(
        OrderedMap<K, V> m1,
        OrderedMap<K, V> m2,
        Action<K, V?> onAdded,
        Action<K, V?> onRemoved,
        Action<K, V?, V?> onModified)
        where K : notnull
    {
        DiffOrderedMapsFunc(m1, m2, static (a, b) => EqualityComparer<V>.Default.Equals(a, b), onAdded, onRemoved, onModified);
    }

    public static void DiffOrderedMapsFunc<K, V>(
        OrderedMap<K, V> m1,
        OrderedMap<K, V> m2,
        Func<V?, V?, bool> equalValues,
        Action<K, V?> onAdded,
        Action<K, V?> onRemoved,
        Action<K, V?, V?> onModified)
        where K : notnull
    {
        foreach (var (key, value2) in m2.Entries())
        {
            if (!m1.Has(key))
            {
                onAdded(key, value2);
            }
        }

        foreach (var (key, value1) in m1.Entries())
        {
            var (value2, ok) = m2.Get(key);
            if (ok)
            {
                if (!equalValues(value1, value2))
                {
                    onModified(key, value1, value2);
                }
            }
            else
            {
                onRemoved(key, value1);
            }
        }
    }
}
