// ported from: internal/collections/syncset.go
//
// 与 Go 的偏离：
//
// 1) Go 的 `m SyncMap[T, struct{}]` 是值字段 → C# 用 readonly 字段持有一个 `SyncMap<T, byte>` 实例
//    （C# 没有零尺寸类型，用 byte 占位表示 Go 的 `struct{}`：只关心"键是否存在"）。
// 2) 零值：`new SyncSet<T>()` 就是 Go 的零值 `SyncSet[T]{}`（Go 的 `collections.SyncSet[T]{}`
//    复合字面量）；实例方法不能在 null 引用上分派，Go 里也没有 nil 接收者分支。
// 3) 泛型约束：内部的 SyncMap 用 ConcurrentDictionary，要求 K 满足 notnull，故加 `where T : notnull`
//    （对应 Go 的 `T comparable`）。
// 4) ToSlice：Go 是 `make([]T, 0, Size())` + Range append；C# 用 `List<T>(Size())` + ToArray()。
//    空集合返回**空数组**（不是 null）—— 与 Go 一致（Go 里 make 出来的不是 nil slice）。
// 5) Size/IsEmpty/ToSlice/Keys 都照 Go 用 Range 走一遍（而不是复用 SyncMap 的 ToMap 之类），
//    遍历次数、近似性语义与 Go 相同。

namespace TypeScript.Collections;

/// <summary>
/// Go 的 <c>SyncSet[T]</c>：并发安全的集合（Go 的 <c>struct{}</c> 值集合，建立在 SyncMap 之上）。
/// </summary>
public sealed class SyncSet<T> where T : notnull
{
    // Go: `m SyncMap[T, struct{}]`（zero value 可用）。
    private readonly SyncMap<T, byte> _m = new();

    public bool Has(T key)
    {
        return _m.Load(key).Ok;
    }

    public void Add(T key)
    {
        AddIfAbsent(key);
    }

    /// <summary>
    /// AddIfAbsent adds the key to the set if it is not already present using LoadOrStore.
    /// It returns true if the key was not already present (opposite of the return value of LoadOrStore).
    /// </summary>
    public bool AddIfAbsent(T key)
    {
        return !_m.LoadOrStore(key, 0).Loaded;
    }

    public void Delete(T key)
    {
        _m.Delete(key);
    }

    public void Range(Func<T, bool> fn)
    {
        _m.Range((key, _) => fn(key));
    }

    /// <summary>
    /// Size returns the approximate number of items in the map.
    /// Note that this is not a precise count, as the map may be modified concurrently while this method is running.
    /// </summary>
    public int Size()
    {
        var count = 0;
        _m.Range((_, _) =>
        {
            count++;
            return true;
        });
        return count;
    }

    public bool IsEmpty()
    {
        var empty = true;
        _m.Range((_, _) =>
        {
            empty = false;
            return false;
        });

        return empty;
    }

    public T[] ToSlice()
    {
        // Go: var arr []T; arr = make([]T, 0, s.m.Size()); Range append
        var arr = new List<T>(_m.Size());
        _m.Range((key, _) =>
        {
            arr.Add(key);
            return true;
        });
        return arr.ToArray();
    }

    /// <summary>
    /// Keys returns the same iterator as SyncMap.Keys()（Go 的 SyncSet.Keys 也是包一层 s.m.Range）。
    /// </summary>
    public IEnumerable<T> Keys()
    {
        return _m.Keys();
    }
}
