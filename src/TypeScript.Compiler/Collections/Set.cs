// ported from: internal/collections/set.go
//
// 与 Go 的偏离（逐条说明；除标注外都是语义等价，只为对齐 C# 的语言特性）：
//
// 1) 底层容器：Go 的 `M map[T]struct{}` → C# 的 `HashSet<T>`。等价且更省内存：map[T]struct{} 每个
//    元素仍占一个 bucket 槽位，HashSet<T> 直接用元素本身（不需要 struct{} 值）。`M` 保持 Go 的
//    **导出字段**语义：public 字段，`Keys()` 返回的就是内部这个 HashSet 本身（调用方改它 == 改 set）。
//    外部用法一一对应：`len(s.M)` → `s.M.Count`、`for k := range s.M` → `foreach (var k in s.M)`、
//    `delete(s.M, k)` → `s.M.Remove(k)`。TSGO 里唯一的包外 `.M` 用法是
//    execute/build/orchestrator.go:443 的 `len(projects.M)`，对应 `projects.M.Count`。
//
// 2) `M` 的可空性：Go 的零值 `Set[T]{}` 的 M 是 nil map（读为空、首次写才 make）。C# 里 M 由构造函数
//    初始化为空集合（`new Set<T>()` 就对应 Go 的零值 `Set[T]{}`），因此 Go 里所有 `s.M == nil` 分支
//    （Add 时新建 map、Delete/Clear 对 nil map 是空操作）在 C# 里等价于"对一个空集合操作"，不再保留。
//    `Keys()` 在 Go 里可能返回 nil（接收者为 nil，或 M 为 nil）；C# 里 M 恒非空，故返回非空引用。
//
// 3) nil 接收者：Go 允许 `(*Set[T])(nil).Has(k)`→false、`Len()`→0、`Keys()`→nil、`Clone()`→nil。
//    C# 的实例方法**不能在 null 引用上分派**（调用即抛 NullReferenceException），这些分支在 C# 里
//    不可达，因此本文件不保留 `if (this is null)`；移植 Go 的调用点时把
//    `var x := s.Has(k)` 写成 `s?.Has(k) ?? false`、`Len()` 写成 `s?.Len() ?? 0`、`Clone()` 写成
//    `s?.Clone()`。**参数**可能为 nil 的分支照 Go 保留（Equals/IsSubsetOf/Intersects/Union/
//    UnionedWith 的 `other == nil`），它们是有语义的、可达的。
//
// 4) 泛型：Go 的 `comparable` 不加 C# 约束。HashSet<T> 不要求 notnull（允许 null 元素），与 Go 的
//    map 一致；比较用默认的 EqualityComparer<T>.Default（等价于 Go 的 == 语义：引用类型未重写
//    Equals 时按引用比较）。

namespace TypeScript.Collections;

/// <summary>
/// Go 的 <c>Set[T]</c>：基于 map 的集合。方法逐条对应
/// Has/Add/Delete/Len/Keys/Clear/AddIfAbsent/Clone/Union/UnionedWith/Equals/IsSubsetOf/Intersects。
/// </summary>
public sealed class Set<T>
{
    /// <summary>Go 的导出字段 <c>M map[T]struct{}</c>（见文件头第 1、2 条）。</summary>
    public HashSet<T> M;

    /// <summary>Go 的零值 <c>Set[T]{}</c>；C# 里对应 <c>new Set&lt;T&gt;()</c>。</summary>
    public Set()
    {
        M = new HashSet<T>();
    }

    // 对应 Go 的 &Set[T]{M: make(map[T]struct{}, hint)}（NewSetWithSizeHint）
    internal Set(int hint)
    {
        M = new HashSet<T>(hint);
    }

    // 对应 Go 的 &Set[T]{M: maps.Clone(s.M)}（Clone）
    internal Set(HashSet<T> m)
    {
        M = m;
    }

    public bool Has(T key)
    {
        return M.Contains(key);
    }

    // Go 的 Add 没有 nil 接收者分支（nil 接收者会在 s.M == nil 处 panic）；C# 的实例方法在 null
    // 接收者上调用直接抛 NullReferenceException，语义一致，故这里也不判空。
    public void Add(T key)
    {
        M.Add(key);
    }

    /// <summary>Go 的 <c>delete(s.M, key)</c>：键不存在时是空操作。</summary>
    public void Delete(T key)
    {
        M.Remove(key);
    }

    public int Len()
    {
        return M.Count;
    }

    /// <summary>
    /// 返回**内部集合本身**（Go 的 <c>Keys()</c> 返回底层 map 本身，调用方可以继续增删/遍历）。
    /// </summary>
    public HashSet<T> Keys()
    {
        return M;
    }

    /// <summary>Go 的 <c>clear(s.M)</c>：清空内容，已分配的空间保留复用。</summary>
    public void Clear()
    {
        M.Clear();
    }

    /// <summary>Returns true if the key was not already present in the set.</summary>
    public bool AddIfAbsent(T key)
    {
        return M.Add(key);
    }

    public Set<T> Clone()
    {
        // Go: maps.Clone(s.M)；带上 M 自己的比较器（外部若换过 M，语义跟随 M）。
        return new Set<T>(new HashSet<T>(M, M.Comparer));
    }

    /// <summary>把 <paramref name="other"/> 的所有元素并入本集合（就地修改）。</summary>
    public void Union(Set<T>? other)
    {
        // Go: if s.Len() == 0 &amp;&amp; other.Len() == 0 { return }（other 为 nil 时 Len() 返回 0）
        if (Len() == 0 && (other is null || other.Len() == 0))
        {
            return;
        }

        // Go: if s == nil { panic("cannot modify nil Set") }；C# 里 this 不可能为 null（见文件头第 3 条），
        // 只剩下 Go 在 other 为 nil 时会踩的空指针路径（maps.Clone(other.M) / maps.Copy(s.M, other.M)）。
        if (other is null)
        {
            throw new NullReferenceException("cannot modify nil Set: other is nil");
        }

        // Go: if s.M == nil { s.M = maps.Clone(other.M); return } + maps.Copy(s.M, other.M)。
        // C# 的 M 恒非空，两条路径合并为 UnionWith（都不与 other.M 共享存储）。
        M.UnionWith(other.M);
    }

    /// <summary>返回本集合与 <paramref name="other"/> 的并集（新集合，不修改两者）。</summary>
    public Set<T> UnionedWith(Set<T>? other)
    {
        // Go: if s == nil &amp;&amp; other == nil { return nil }；C# 里 this 不可能为 null，故恒返回新集合。
        // Go: result := s.Clone(); if result == nil { result = &Set[T]{} } —— this 非 null 时 Clone 必非 null。
        var result = Clone();
        if (other is not null)
        {
            // Go: if result.M == nil { result.M = make(map[T]struct{}, len(other.M)) }; maps.Copy(result.M, other.M)
            result.M.UnionWith(other.M);
        }

        return result;
    }

    /// <summary>Go 的 <c>maps.Equal(s.M, other.M)</c>：键集合相同即相等。</summary>
    public bool Equals(Set<T>? other)
    {
        // Go: if s == other { return true }（指针相等）
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        // Go: if s == nil || other == nil { return false }
        if (other is null)
        {
            return false;
        }

        return M.SetEquals(other.M);
    }

    public bool IsSubsetOf(Set<T>? other)
    {
        foreach (var key in M)
        {
            // Go 用 other.Has(key) —— other 为 nil 时 Has 返回 false；C# 的实例方法不能在 null 上分派，
            // 所以这里显式判空。
            if (other is null || !other.Has(key))
            {
                return false;
            }
        }

        return true;
    }

    public bool Intersects(Set<T>? other)
    {
        // Go: if s == nil || other == nil { return false }
        if (other is null)
        {
            return false;
        }

        return M.Overlaps(other.M);
    }
}

public static partial class Collections
{
    /// <summary>Creates a new Set with a hint for the number of elements it will contain.</summary>
    public static Set<T> NewSetWithSizeHint<T>(int hint)
    {
        return new Set<T>(hint);
    }

    public static Set<T> NewSetFromItems<T>(params T[] items)
    {
        var s = new Set<T>();
        foreach (var item in items)
        {
            s.Add(item);
        }

        return s;
    }
}
