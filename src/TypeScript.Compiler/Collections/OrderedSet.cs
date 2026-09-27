// ported from: internal/collections/ordered_set.go
//
// 与 Go 的偏离：
//
// 1) Go 的 `m OrderedMap[T, struct{}]` 是**值字段** → C# 用 readonly 字段持有一个
//    `OrderedMap<T, byte>` 实例（C# 没有零尺寸类型，用 byte 占位表示 Go 的 `struct{}`：只关心
//    "键是否存在"，值本身无意义）。
// 2) Go 的零值 OrderedSet 的 m 是零值 OrderedMap（keys/mp 都是 nil，首次写才分配）；C# 在构造函数里
//    建好空 OrderedMap，所以 `new OrderedSet<T>()` 就是 Go 的零值 `OrderedSet[T]{}`。
// 3) nil 接收者：C# 的实例方法不能在 null 引用上分派，Go 的 nil 分支不可达（理由见 Set.cs 文件头第 3 条）。
// 4) 泛型约束：内部用 Dictionary 的 OrderedMap 要求 K 满足 notnull，故这里加 `where T : notnull`
//    （对应 Go 的 `T comparable`）。
using static TypeScript.Collections.Collections;

namespace TypeScript.Collections;

/// <summary>
/// Go 的 <c>OrderedSet[T]</c>：插入有序的集合（内部就是键为 T、值为 <c>struct{}</c> 的 OrderedMap）。
/// </summary>
public sealed class OrderedSet<T> where T : notnull
{
    private readonly OrderedMap<T, byte> m;

    /// <summary>Go 的零值 <c>OrderedSet[T]{}</c>；C# 里对应 <c>new OrderedSet&lt;T&gt;()</c>。</summary>
    public OrderedSet()
    {
        m = new OrderedMap<T, byte>();
    }

    // 对应 Go 的 &OrderedSet[T]{m: newMapWithSizeHint[T, struct{}](hint)}（NewOrderedSetWithSizeHint）
    internal OrderedSet(int hint)
    {
        m = NewMapWithSizeHint<T, byte>(hint);
    }

    // 对应 Go 的 &OrderedSet[T]{m: s.m.clone()}（Clone）
    internal OrderedSet(OrderedMap<T, byte> m)
    {
        this.m = m;
    }

    /// <summary>Add adds a value to the set.（新值追加到末尾；已有值位置不变）</summary>
    public void Add(T value)
    {
        m.Set(value, 0);
    }

    /// <summary>Has returns true if the set contains the value.</summary>
    public bool Has(T value)
    {
        return m.Has(value);
    }

    /// <summary>Delete removes a value from the set.</summary>
    public bool Delete(T value)
    {
        return m.Delete(value).Ok;
    }

    /// <summary>Values returns an iterator over the values in the set.（Go: <c>iter.Seq[T]</c>）</summary>
    public IEnumerable<T> Values()
    {
        return m.Keys();
    }

    /// <summary>Clear removes all elements from the set. The space allocated for the set will be reused.</summary>
    public void Clear()
    {
        m.Clear();
    }

    /// <summary>Size returns the number of elements in the set.</summary>
    public int Size()
    {
        return m.Size();
    }

    /// <summary>Clone returns a shallow copy of the set.</summary>
    public OrderedSet<T> Clone()
    {
        return new OrderedSet<T>(m.Clone());
    }
}

public static partial class Collections
{
    /// <summary>Creates a new OrderedSet with a hint for the number of elements it will contain.</summary>
    public static OrderedSet<T> NewOrderedSetWithSizeHint<T>(int hint) where T : notnull
    {
        return new OrderedSet<T>(hint);
    }
}
