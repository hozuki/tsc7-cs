// ported from: internal/core/core.go
//
// 与 Go 的差异（逐条给依据；除标注"语义偏离"的以外都是 C# 语言特性造成的等价映射）：
//
// 1) iter.Seq[T] → IEnumerable<T>、iter.Seq2[int, T] → IEnumerable<(int Index, T Value)>，用 yield return
//    实现（Go 的 push 迭代器用 `yield(...) == false` 提前退出，C# 的拉取式 foreach 用 break 表达同一件事）。
//    代价：Go 可以把 iter.Seq 的 nil 当"空序列"（ForEach/Nil 都检查 nil），C# 的引用就不能为 null 时才更安全，
//    所以参数写成 `IEnumerable<T>?` 并逐个保留 Go 的 nil 检查。
//
// 2) 切片参数与返回值：参数一律 `T[]?`（Go 的 nil 切片在这些函数里只参与 len()/range，或者被原样返回，
//    所以 C# 允许传 null，null 与空数组等价）；返回值一律 `T[]`（Go 的 nil 返回值在 C# 里用**空数组**表达，
//    因为 C# 的调用点对返回值做 .Length/foreach，用 T[]? 会让每个调用点都多一次空检查）。
//    唯一可见差异：Go 里 `slice == nil` 时返回 nil 的那些函数（Map/Filter/…）在 C# 里返回空数组——
//    两者 len 都是 0、传给 Same 都判等（见下），可观察行为一致。
//
// 3) Same：Go 比较 `&s1[0] == &s2[0]`（同一底层数组，长度也相同）；C# 的数组没有"子切片视图"概念，
//    等价物是引用相等 ReferenceEquals（长度相同的情况下）。长度都为 0 时 Go 直接返回 true（C# 同）。
//    **不要与 `slices.Equal` 混用**：`slices.Equal` 是逐元素比较 → `SameElements`（两者 Go 自己就分得很清，
//    例如 `checker.go:19452` 的 `slices.Equal(typeParameters, typeArguments)` 与 `checker.go:22590` 的
//    `core.Same(newTypeArguments, resolvedTypeArguments)`）。把 `slices.Equal` 写成 `Same` 会静默退化成
//    "几乎永远 false"，曾导致 `resolveObjectTypeMembers` 对未实例化泛型做整表恒等实例化（详见
//    docs/architecture.md §3.12 真因 A）。移植新调用点时：Go 写 `slices.Equal`/`slices.Compare` 的一律用
//    `SameElements`（或 `SequenceEqual`/`SequenceCompareTo`），Go 写 `core.Same` 的才用 `Same`。
//
// 4) Go 的 `comparable` / `==`（MapNonNil / SameMap / UnorderedEqual / Deduplicate / OrElse / FirstNonZero /
//    AppendIfUnique / MapFiltered 的 key 等）用 `EqualityComparer<T>.Default`（值类型字段级、引用类型引用级；
//    实现了 IEquatable<T> 的类型用其 Equals），并对字典/集合加 `where K : notnull`（C# 的 Dictionary<K,V>
//    不接受可空引用类型实参，见 Collections.OrderedMap 的处理）。
//
// 5) Go 的回调返回 `(U, bool)`（MapFiltered）在 C# 里是 `Func<T, (U Value, bool Ok)>`——回调参数没法用 out；
//    而**包级函数**自己的 `(T, bool)` 按项目约定（TSPath/Path.cs 文件头）映射成 `bool TryX(..., out T)`。
//
// 6) Go 的多返回值 + error 在 C# 里是"正常路径返回值 + 异常"（docs §3.2），于是三个 Go 语言的产物在本文件
//    **没有对应物**，按"Go 语言特性在 C# 里没有对应物"处理（不是漏移植）：
//    - `Must[T](v, err)`（core.go:510）：Go 用来吃掉 error 返回值（core.Must(core.StringifyJson(…))、
//      core.Must(os.Getwd())）。C# 里被调用的函数本身就没有 error（StringifyJson 直接返回 string、
//      Directory.GetCurrentDirectory() 直接返回 string），调用点写 `Must(f())` 无法产生任何合法实参。
//    - `TryMap[T,U](slice, f) ([]U, error)`（core.go:91）：C# 里 f 失败就是抛异常，语义与 Map 完全相同
//      （部分结果作废），保留它只会得到一个与 Map 逐字相同的死 API。
//    - `FirstResult[T1](t1, _ ...any)`（core.go:515）：Go 用来取多返回值里的第一个（如
//      `core.FirstResult(utf8.DecodeRuneInString(name))`）。C# 的多返回值是元组/out 参数，
//      `FirstResult(tuple)` 无法通过类型检查（元组不能当作 T1），调用点直接写相应 API 即可。
//
// 7) `ApplyDebugStackLimit`：Go 读 TS_GO_DEBUG_STACK_LIMIT 并调用 runtime/debug.SetMaxStack。.NET 没有
//    等价 API（.NET Core 已去掉 Thread 的 maxStackSize 构造参数），所以这里只做与 Go 相同的解析/校验，
//    结果记在内部的 DebugStackLimit 里（M8 的 CLI 若把主线程改成自带栈大小的线程，可从这里取值）。
//
// 8) 拼写建议：Go 的 `runeName := []rune(name)` 是码点数组、而 `len(candidateName)` 是**字节**长度（同一表达式里混了
//    两种口径）；C# 里 runeName 用码点数组（ToCodePoints）、candidateName.Length 用 UTF-16 代码单元数。
//    ASCII 名称下两者完全相同；非 ASCII 名称的候选可能与 Go 给出不同建议（Go 的字节口径在 C# 里没有对应物）。
//    长度差异上限/最大距离这两个阈值按 Go 的算式逐字保留（都基于 runeName 的码点数）。
//
// 9) levenshtein 缓冲区池：Go 用 sync.Pool（每个 Get 独占一份，重入时 Get 会给出另一份）。C# 用
//    [ThreadStatic] 缓存 + 取出时置空，得到同样的"每线程一份、取用期间独占、重入时另分配"的语义（无锁、无分配）。
//
// 10) DiffMapsFunc：Go 在 onRemoved == nil 且存在"m1 有而 m2 没有"的键时，会直接调用 nil 函数——**panic**。
//     这是 Go 侧潜伏的 bug（现有两个调用点都传了 onRemoved，所以从未触发）；C# 侧改成跳过该回调，
//     不复刻这个崩溃（不改变任何现存调用点的行为）。
//
// 11) StringifyJson 的 `(string, error)` → 只返回 string（错误时 MarshalIndent 抛 JsonException，docs §3.2）。
//
// 12) 导出的 Go 包级常量/类型之外没有新增 API；`DebugStackLimit` 是 internal（不算公开 API）。
//
// 13) 零值口径的已知落差（T = string 时）：Go 的 string 零值是 ""，而 C# 无约束泛型只能给出 default(T)
//     （= null）。所以 FirstOrNil/LastOrNil/ElementOrNil/FirstOrNilSeq/FirstNonNil/Find/
//     FindLast/MapNonNil 在 **T = string** 时与 Go 不同（null 而不是 ""）。已核对 TSGO 的调用点：
//     这些函数实际都用指针/引用类型（*ast.Node、*Symbol、*Type、符号对象…），nil 与 Go 的零值一致；
//     string 特化的那个 API（GetSpellingSuggestionForStrings）已把零值补回 ""。移植新的调用点时若元素是
//     string，请把 Go 的 `x != ""` 写成 `!string.IsNullOrEmpty(x)`。
//     **例外：OrElse / FirstNonZero** —— 两者各有 string 专用重载（Core.cs:733-740、544-558），对 string 与
//     Go 的零值完全一致（null/"" 都是零值），所以 string 实参的调用点直接照抄 Go 即可。
using System.Globalization;
using System.Text;
using static TypeScript.Json.Json;
using static TypeScript.StringUtil.StringUtil;
using static TypeScript.TSPath.TSPath;

namespace TypeScript.Core;

/// <summary>Go 的 <c>type UTF16Offset int</c>：以 UTF-16 代码单元计的偏移量。</summary>
public readonly struct UTF16Offset : IEquatable<UTF16Offset>
{
    public readonly int Value;

    public UTF16Offset(int value)
    {
        Value = value;
    }

    public static implicit operator int(UTF16Offset offset) => offset.Value;

    public static implicit operator UTF16Offset(int value) => new(value);

    public bool Equals(UTF16Offset other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is UTF16Offset other && Equals(other);

    public override int GetHashCode() => Value;

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

public static partial class Core
{
    /// <summary>TS_GO_DEBUG_STACK_LIMIT 解析出来的值（.NET 没有 runtime/debug.SetMaxStack 的对应物，见文件头 7）。</summary>
    internal static int DebugStackLimit { get; private set; }

    public static void ApplyDebugStackLimit()
    {
        var v = Environment.GetEnvironmentVariable("TS_GO_DEBUG_STACK_LIMIT");
        if (string.IsNullOrEmpty(v))
        {
            return;
        }

        // Go 的 strconv.Atoi：十进制、允许前导符号、不接受空白/千分位。
        if (!int.TryParse(v, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) || n <= 0)
        {
            return;
        }

        DebugStackLimit = n;
    }

    public static T[] Filter<T>(T[]? slice, Func<T, bool> f)
    {
        if (slice == null)
        {
            return [];
        }

        for (var i = 0; i < slice.Length; i++)
        {
            if (!f(slice[i]))
            {
                var result = new List<T>(i);
                for (var j = 0; j < i; j++)
                {
                    result.Add(slice[j]);
                }

                for (i++; i < slice.Length; i++)
                {
                    var value = slice[i];
                    if (f(value))
                    {
                        result.Add(value);
                    }
                }

                return result.ToArray();
            }
        }

        return slice;
    }

    public static IEnumerable<T> FilterSeq<T>(T[]? slice, Func<T, bool> f)
    {
        if (slice == null)
        {
            yield break;
        }

        foreach (var value in slice)
        {
            if (f(value))
            {
                yield return value;
            }
        }
    }

    public static T[] FilterIndex<T>(T[]? slice, Func<T, int, T[], bool> f)
    {
        if (slice == null)
        {
            return [];
        }

        for (var i = 0; i < slice.Length; i++)
        {
            if (!f(slice[i], i, slice))
            {
                var result = new List<T>(i);
                for (var j = 0; j < i; j++)
                {
                    result.Add(slice[j]);
                }

                for (i++; i < slice.Length; i++)
                {
                    var value = slice[i];
                    if (f(value, i, slice))
                    {
                        result.Add(value);
                    }
                }

                return result.ToArray();
            }
        }

        return slice;
    }

    public static U[] Map<T, U>(T[]? slice, Func<T, U> f)
    {
        if (slice == null)
        {
            return [];
        }

        var result = new U[slice.Length];
        for (var i = 0; i < slice.Length; i++)
        {
            result[i] = f(slice[i]);
        }

        return result;
    }

    public static U[] MapIndex<T, U>(T[]? slice, Func<T, int, U> f)
    {
        if (slice == null)
        {
            return [];
        }

        var result = new U[slice.Length];
        for (var i = 0; i < slice.Length; i++)
        {
            result[i] = f(slice[i], i);
        }

        return result;
    }

    public static U[] MapNonNil<T, U>(T[]? slice, Func<T, U> f)
    {
        var result = new List<U>();
        if (slice != null)
        {
            foreach (var value in slice)
            {
                var mapped = f(value);
                if (!EqualityComparer<U>.Default.Equals(mapped, default!))
                {
                    result.Add(mapped);
                }
            }
        }

        return result.ToArray();
    }

    public static U[] MapFiltered<T, U>(T[]? slice, Func<T, (U Value, bool Ok)> f)
    {
        var result = new List<U>();
        if (slice != null)
        {
            foreach (var value in slice)
            {
                var (mapped, ok) = f(value);
                if (!ok)
                {
                    continue;
                }

                result.Add(mapped);
            }
        }

        return result.ToArray();
    }

    public static U[] FlatMap<T, U>(T[]? slice, Func<T, U[]> f)
    {
        var result = new List<U>();
        if (slice != null)
        {
            foreach (var value in slice)
            {
                var mapped = f(value);
                if (mapped.Length != 0)
                {
                    result.AddRange(mapped);
                }
            }
        }

        return result.ToArray();
    }

    public static T[] SameMap<T>(T[]? slice, Func<T, T> f)
    {
        if (slice == null)
        {
            return [];
        }

        for (var i = 0; i < slice.Length; i++)
        {
            var value = slice[i];
            var mapped = f(value);
            if (!EqualityComparer<T>.Default.Equals(mapped, value))
            {
                var result = new T[slice.Length];
                Array.Copy(slice, result, i);
                result[i] = mapped;
                for (var j = i + 1; j < slice.Length; j++)
                {
                    result[j] = f(slice[j]);
                }

                return result;
            }
        }

        return slice;
    }

    public static T[] SameMapIndex<T>(T[]? slice, Func<T, int, T> f)
    {
        if (slice == null)
        {
            return [];
        }

        for (var i = 0; i < slice.Length; i++)
        {
            var value = slice[i];
            var mapped = f(value, i);
            if (!EqualityComparer<T>.Default.Equals(mapped, value))
            {
                var result = new T[slice.Length];
                Array.Copy(slice, result, i);
                result[i] = mapped;
                for (var j = i + 1; j < slice.Length; j++)
                {
                    result[j] = f(slice[j], j);
                }

                return result;
            }
        }

        return slice;
    }

    public static bool Same<T>(T[]? s1, T[]? s2)
    {
        var len1 = s1?.Length ?? 0;
        var len2 = s2?.Length ?? 0;
        if (len1 == len2)
        {
            // Go: len(s1) == 0 || &s1[0] == &s2[0]（同一底层数组）。
            return len1 == 0 || ReferenceEquals(s1, s2);
        }

        return false;
    }

    /// <summary>
    /// Go: <c>slices.Equal(s1, s2)</c> —— **逐元素**比较（元素语义同 Go 的 <c>==</c>，即引用类型按引用相等，
    /// 见文件头第 4 条）。注意与 <see cref="Same"/> 的区别：<c>Same</c> 映射的是 <c>core.Same</c>（比较
    /// 「同一底层数组」），两者**不可互换**（Go 自己在这两个函数的选择上是有意的，例如
    /// <c>checker.go:19452</c> 的 <c>slices.Equal(typeParameters, typeArguments)</c> 逐元素比较，而
    /// <c>checker.go:22590</c> 的 <c>core.Same(newTypeArguments, resolvedTypeArguments)</c> 比较同一数组）。
    /// </summary>
    public static bool SameElements<T>(T[]? s1, T[]? s2)
    {
        var len1 = s1?.Length ?? 0;
        if (len1 != (s2?.Length ?? 0))
        {
            return false;
        }

        if (len1 == 0)
        {
            return true;
        }

        // 用 .NET 内置的 span 序列比较：对 `int`/`char`/`uint` 等基元元素会走**向量化**路径（BCL 的
        // `MemoryExtensions.SequenceEqual` 有 intrinsic 实现），其余元素类型退化为 `EqualityComparer<T>.Default`
        // 逐元素比较 —— 与 Go `slices.Equal` 的元素语义一致（引用类型按引用、值类型按 `IEquatable<T>`/字段），
        // 且省掉了手写循环的每次边界检查。注意上方的长度检查已保证两个 span 等长。
        return s1.AsSpan(0, len1).SequenceEqual(s2.AsSpan(0, len1));
    }

    public static bool Some<T>(T[]? slice, Func<T, bool> f)
    {
        if (slice != null)
        {
            foreach (var value in slice)
            {
                if (f(value))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static bool Every<T>(T[]? slice, Func<T, bool> f)
    {
        if (slice != null)
        {
            foreach (var value in slice)
            {
                if (!f(value))
                {
                    return false;
                }
            }
        }

        return true;
    }

    public static Func<T, bool> Or<T>(params Func<T, bool>[] funcs)
    {
        return input =>
        {
            foreach (var f in funcs)
            {
                if (f(input))
                {
                    return true;
                }
            }

            return false;
        };
    }

    public static T Find<T>(T[]? slice, Func<T, bool> f)
    {
        if (slice != null)
        {
            foreach (var value in slice)
            {
                if (f(value))
                {
                    return value;
                }
            }
        }

        return default!;
    }

    public static T FindLast<T>(T[]? slice, Func<T, bool> f)
    {
        if (slice != null)
        {
            for (var i = slice.Length - 1; i >= 0; i--)
            {
                var value = slice[i];
                if (f(value))
                {
                    return value;
                }
            }
        }

        return default!;
    }

    public static int FindIndex<T>(T[]? slice, Func<T, bool> f)
    {
        if (slice != null)
        {
            for (var i = 0; i < slice.Length; i++)
            {
                if (f(slice[i]))
                {
                    return i;
                }
            }
        }

        return -1;
    }

    public static int FindLastIndex<T>(T[]? slice, Func<T, bool> f)
    {
        if (slice != null)
        {
            for (var i = slice.Length - 1; i >= 0; i--)
            {
                if (f(slice[i]))
                {
                    return i;
                }
            }
        }

        return -1;
    }

    public static T FirstOrNil<T>(T[]? slice)
    {
        if (slice != null && slice.Length != 0)
        {
            return slice[0];
        }

        return default!;
    }

    public static T LastOrNil<T>(T[]? slice)
    {
        if (slice != null && slice.Length != 0)
        {
            return slice[^1];
        }

        return default!;
    }

    public static T ElementOrNil<T>(T[]? slice, int index)
    {
        if (slice != null && index < slice.Length)
        {
            return slice[index];
        }

        return default!;
    }

    public static T FirstOrNilSeq<T>(IEnumerable<T>? seq)
    {
        if (seq != null)
        {
            foreach (var value in seq)
            {
                return value;
            }
        }

        return default!;
    }

    public static U FirstNonNil<T, U>(T[]? slice, Func<T, U> f)
    {
        if (slice != null)
        {
            foreach (var value in slice)
            {
                var mapped = f(value);
                if (!EqualityComparer<U>.Default.Equals(mapped, default!))
                {
                    return mapped;
                }
            }
        }

        return default!;
    }

    public static T FirstNonZero<T>(params ReadOnlySpan<T> values)
    {
        foreach (var value in values)
        {
            if (!EqualityComparer<T>.Default.Equals(value, default!))
            {
                return value;
            }
        }

        return default!;
    }

    // Go: `FirstNonZero[T comparable]`（core.go:295-303）取第一个**非零值**，零值判据是 `value != zero` ——
    // `T = string` 时零值是 `""`（不是 nil）。C# 的无约束泛型只能给出 default(T)（string 为 null），会把 `""`
    // 误判成"非零值"直接返回，故 string 单独重载（见文件头第 13 条）：null 与 "" 都算零值，全零时返回 Go 的零值 ""。
    public static string FirstNonZero(params ReadOnlySpan<string?> values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }
        }

        return "";
    }

    public static T[] Concatenate<T>(T[]? s1, T[]? s2)
    {
        if (s2 == null || s2.Length == 0)
        {
            return s1 ?? [];
        }

        if (s1 == null || s1.Length == 0)
        {
            return s2;
        }

        var result = new T[s1.Length + s2.Length];
        Array.Copy(s1, 0, result, 0, s1.Length);
        Array.Copy(s2, 0, result, s1.Length, s2.Length);
        return result;
    }

    public static T[] Splice<T>(T[]? s1, int start, int deleteCount, params ReadOnlySpan<T> items)
    {
        s1 ??= [];
        if (start < 0)
        {
            start = s1.Length + start;
        }

        if (start < 0)
        {
            start = 0;
        }

        if (start > s1.Length)
        {
            start = s1.Length;
        }

        if (deleteCount < 0)
        {
            deleteCount = 0;
        }

        var end = Math.Min(start + deleteCount, s1.Length);
        if (start == end && items.Length == 0)
        {
            return s1;
        }

        var result = new T[start + items.Length + (s1.Length - end)];
        Array.Copy(s1, 0, result, 0, start);
        items.CopyTo(result.AsSpan(start));
        Array.Copy(s1, end, result, start + items.Length, s1.Length - end);
        return result;
    }

    public static int CountWhere<T>(T[]? slice, Func<T, bool> f)
    {
        var count = 0;
        if (slice != null)
        {
            foreach (var value in slice)
            {
                if (f(value))
                {
                    count++;
                }
            }
        }

        return count;
    }

    public static T[] ReplaceElement<T>(T[] slice, int i, T t)
    {
        var result = (T[])slice.Clone();
        result[i] = t;
        return result;
    }

    public static T[] InsertSorted<T>(T[] slice, T element, Comparison<T> cmp)
    {
        var i = BinarySearchFuncIndex(slice, element, cmp);
        var result = new T[slice.Length + 1];
        Array.Copy(slice, result, i);
        result[i] = element;
        Array.Copy(slice, i, result, i + 1, slice.Length - i);
        return result;
    }

    // Go 的 slices.BinarySearchFunc 的插入点（返回值的第一个分量）；InsertSorted 是它唯一的用处。
    private static int BinarySearchFuncIndex<T>(T[] slice, T target, Comparison<T> cmp)
    {
        var low = 0;
        var high = slice.Length;
        while (low < high)
        {
            var mid = low + ((high - low) >> 1);
            var c = cmp(slice[mid], target);
            if (c < 0)
            {
                low = mid + 1;
            }
            else if (c > 0)
            {
                high = mid;
            }
            else
            {
                return mid;
            }
        }

        return low;
    }

    // MinAllFunc returns all minimum elements from xs according to the comparison function cmp.
    public static T[] MinAllFunc<T>(T[]? xs, Comparison<T> cmp)
    {
        if (xs == null || xs.Length == 0)
        {
            return [];
        }

        var m = xs[0];
        var mins = new List<T> { xs[0] };

        for (var i = 1; i < xs.Length; i++)
        {
            var x = xs[i];
            var c = cmp(x, m);
            if (c < 0)
            {
                m = x;
                mins.Clear();
                mins.Add(x);
            }
            else if (c == 0)
            {
                mins.Add(x);
            }
        }

        return mins.ToArray();
    }

    public static T[] AppendIfUnique<T>(T[]? slice, T element)
    {
        if (slice == null)
        {
            return [element];
        }

        if (Array.IndexOf(slice, element) >= 0)
        {
            return slice;
        }

        var result = new T[slice.Length + 1];
        Array.Copy(slice, result, slice.Length);
        result[slice.Length] = element;
        return result;
    }

    public static Func<T> Memoize<T>(Func<T>? create)
    {
        T value = default!;
        return () =>
        {
            if (create != null)
            {
                value = create();
                create = null;
            }

            return value;
        };
    }

    // Returns whenTrue if b is true; otherwise, returns whenFalse. IfElse should only be used when branches are either
    // constant or precomputed as both branches will be evaluated regardless as to the value of b.
    public static T IfElse<T>(bool b, T whenTrue, T whenFalse)
    {
        return b ? whenTrue : whenFalse;
    }

    // Returns value if value is not the zero value of T; Otherwise, returns defaultValue. OrElse should only be used when
    // defaultValue is constant or precomputed as its argument will be evaluated regardless as to the content of value.
    public static T OrElse<T>(T value, T defaultValue)
    {
        return EqualityComparer<T>.Default.Equals(value, default!) ? defaultValue : value;
    }

    // Go: `OrElse[T comparable]`（core.go:409-414）判零用的是 `*new(T)`：`T = string` 时零值是 `""`（**不是** nil）。
    // C# 的无约束泛型只能给出 `default(T)`（string 为 null），会把 `""` 误判成"有值"，故 string 单独重载以保持
    // Go 的零值语义（见文件头第 13 条）。C# 的 null 与 Go 的 "" 是同一个"零值"（Go 的 string 没有 nil）⇒ 两者都回退。
    // 非泛型重载在参数类型相同时优先于泛型重载，所以所有 string 实参的调用点自动落到这里；没有装箱、没有反射。
    public static string? OrElse(string? value, string? defaultValue)
    {
        return string.IsNullOrEmpty(value) ? defaultValue : value;
    }

    // Returns `a` if `a` is not `nil`; Otherwise, returns `b`. Coalesce is roughly analogous to `??` in JS, except that it
    // non-shortcutting, so it is advised to only use a constant or precomputed value for `b`
    public static T? Coalesce<T>(T? a, T? b)
        where T : class
    {
        return a ?? b;
    }

    public static ECMALineStarts ComputeECMALineStarts(string text)
    {
        var result = new List<TextPos>(CountNewlines(text) + 1);
        foreach (var pos in ComputeECMALineStartsSeq(text))
        {
            result.Add(pos);
        }

        return new ECMALineStarts(result.ToArray());
    }

    // Go 的 strings.Count(text, "\n")。
    private static int CountNewlines(string text)
    {
        var count = 0;
        foreach (var ch in text)
        {
            if (ch == '\n')
            {
                count++;
            }
        }

        return count;
    }

    public static IEnumerable<TextPos> ComputeECMALineStartsSeq(string text)
    {
        // Go 按 UTF-8 字节推进；C# 按 UTF-16 代码单元推进（docs §3.1），换行判定与"CRLF 算一行"的语义一致。
        var textLen = text.Length;
        var pos = 0;
        var lineStart = 0;
        while (pos < textLen)
        {
            var b = text[pos];
            if (b < 0x80) // Go 的 utf8.RuneSelf
            {
                pos++;
                switch (b)
                {
                    case '\r':
                        if (pos < textLen && text[pos] == '\n')
                        {
                            pos++;
                        }

                        goto case '\n';
                    case '\n':
                        yield return new TextPos(lineStart);
                        lineStart = pos;
                        break;
                }
            }
            else
            {
                _ = Rune.DecodeFromUtf16(text.AsSpan(pos), out var ch, out var size);
                pos += size;
                if (IsLineBreak(ch.Value))
                {
                    yield return new TextPos(lineStart);
                    lineStart = pos;
                }
            }
        }

        yield return new TextPos(lineStart);
    }

    // PositionToLineAndByteOffset returns the 0-based line and byte offset from the
    // start of that line for the given byte position, using the provided line starts.
    // The byte offset is a raw UTF-8 byte offset from the line start, not a UTF-16 code unit count.
    //
    // C# 侧：position/lineStarts 都是 UTF-16 代码单元下标（见 Text.cs 文件头），所以返回的第二个分量是
    // "从行首起的代码单元偏移"；对 ASCII 文本与 Go 的字节偏移完全相同。
    public static (int Line, int ByteOffset) PositionToLineAndByteOffset(int position, TextPos[] lineStarts)
    {
        // Go 的 sort.Search(len, f)-1，其中 f(i) = lineStarts[i] > position；结果再与 0 取 max。
        var line = Math.Max(SearchLineStart(lineStarts, position) - 1, 0);
        return (line, position - lineStarts[line].Value);
    }

    // Go 的 sort.Search(len(lineStarts), func(i) { return int(lineStarts[i]) > position })。
    private static int SearchLineStart(TextPos[] lineStarts, int position)
    {
        var low = 0;
        var high = lineStarts.Length;
        while (low < high)
        {
            var mid = low + ((high - low) >> 1);
            if (lineStarts[mid].Value > position)
            {
                high = mid;
            }
            else
            {
                low = mid + 1;
            }
        }

        return low;
    }

    // UTF16Len returns the number of UTF-16 code units needed to
    // represent the given UTF-8 encoded string.
    public static UTF16Offset UTF16Len(string s)
    {
        // Go 要遍历 UTF-8 字节并按 RuneLen 累加（ASCII 前缀走快路径）；C# 的 string 本身就是 UTF-16
        // 代码单元序列，因此这个换算退化成 s.Length（docs §3.1）。
        return new UTF16Offset(s.Length);
    }

    public static T[] Flatten<T>(T[]?[]? array)
    {
        var result = new List<T>();
        if (array != null)
        {
            foreach (var subArray in array)
            {
                if (subArray != null)
                {
                    result.AddRange(subArray);
                }
            }
        }

        return result.ToArray();
    }

    public static string StringifyJson(object? input, string prefix, string indent)
    {
        // Go 返回 (string, error)：错误路径在 C# 里是 MarshalIndent 抛 JsonException。
        var output = MarshalIndent(input, prefix, indent);
        return Encoding.UTF8.GetString(output);
    }

    public static ScriptKind GetScriptKindFromFileName(string fileName)
    {
        var dotPos = fileName.LastIndexOf('.');
        if (dotPos >= 0)
        {
            switch (ToLowerJS(fileName[dotPos..]))
            {
                case ExtensionJs:
                case ExtensionCjs:
                case ExtensionMjs:
                    return ScriptKind.JS;
                case ExtensionJsx:
                    return ScriptKind.JSX;
                case ExtensionTs:
                case ExtensionCts:
                case ExtensionMts:
                    return ScriptKind.TS;
                case ExtensionTsx:
                    return ScriptKind.TSX;
                case ExtensionJson:
                    return ScriptKind.JSON;
            }
        }

        return ScriptKind.Unknown;
    }

    public static string GetDefaultExtensionForScriptKind(ScriptKind scriptKind)
    {
        switch (scriptKind)
        {
            case ScriptKind.JS:
                return ExtensionJs;
            case ScriptKind.JSX:
                return ExtensionJsx;
            case ScriptKind.TSX:
                return ExtensionTsx;
            case ScriptKind.JSON:
                return ExtensionJson;
            default:
                return ExtensionTs;
        }
    }

    // EnsureScriptKindFromFileName is like GetScriptKindFromFileName, but defaults to
    // ScriptKindTS when the file name has no recognized extension (e.g. files included
    // with allowNonTsExtensions), so the result is always safe to hand to the parser.
    public static ScriptKind EnsureScriptKindFromFileName(string fileName)
    {
        var kind = GetScriptKindFromFileName(fileName);
        if (kind != ScriptKind.Unknown)
        {
            return kind;
        }

        return ScriptKind.TS;
    }

    // Given a name and a list of names that are *not* equal to the name, return a spelling suggestion if there is one that is close enough.
    // Names less than length 3 only check for case-insensitive equality.
    //
    // find the candidate with the smallest Levenshtein distance,
    //
    //	except for candidates:
    //	  * With no name
    //	  * Whose length differs from the target name by more than 0.34 of the length of the name.
    //	  * Whose levenshtein distance is more than 0.4 of the length of the name
    //	    (0.4 allows 1 substitution/transposition for every 5 characters,
    //	     and 1 insertion/deletion at 3 characters)
    //
    // @internal
    public static T GetSpellingSuggestion<T>(string name, IEnumerable<T>? candidates, Func<T, string> getName, Comparison<T> compare)
    {
        return GetSpellingSuggestion(name, candidates, getName, compare, 0 /*maxCandidates*/);
    }

    public static T GetSpellingSuggestionWithMaxCandidateCount<T>(string name, IEnumerable<T>? candidates, Func<T, string> getName, Comparison<T> compare, int maxCandidates)
    {
        return GetSpellingSuggestion(name, candidates, getName, compare, maxCandidates);
    }

    private static T GetSpellingSuggestion<T>(string name, IEnumerable<T>? candidates, Func<T, string> getName, Comparison<T> compare, int maxCandidates)
    {
        var runeName = ToCodePoints(name);
        var maximumLengthDifference = Math.Max(2, (int)(runeName.Length * 0.34));
        var bestDistance = Math.Floor(runeName.Length * 0.4) + 0.9; // If the best result is worse than this, don't bother.
        var buffers = RentLevenshteinBuffers();
        try
        {
            T bestCandidate = default!;
            var hasBest = false;
            var checkedCandidates = 0;
            foreach (var candidate in candidates ?? Array.Empty<T>())
            {
                checkedCandidates++;
                if (maxCandidates > 0 && checkedCandidates > maxCandidates)
                {
                    return default!;
                }

                var candidateName = getName(candidate);
                var maxLen = Math.Max(candidateName.Length, runeName.Length);
                var minLen = Math.Min(candidateName.Length, runeName.Length);
                if (candidateName.Length != 0 && maxLen - minLen <= maximumLengthDifference)
                {
                    if (candidateName == name)
                    {
                        continue;
                    }

                    // Only consider candidates less than 3 characters long when they differ by case.
                    // Otherwise, don't bother, since a user would usually notice differences of a 2-character name.
                    if (candidateName.Length < 3 && !EquateStringCaseInsensitive(candidateName, name))
                    {
                        continue;
                    }

                    var distance = LevenshteinWithMax(buffers, runeName, ToCodePoints(candidateName), bestDistance);
                    if (distance < 0)
                    {
                        continue;
                    }

                    Debug.Assert(distance <= bestDistance); // Else `levenshteinWithMax` should return undefined
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestCandidate = candidate;
                        hasBest = true;
                    }
                    else if (!hasBest || compare(candidate, bestCandidate) < 0)
                    {
                        bestCandidate = candidate;
                        hasBest = true;
                    }
                }
            }

            return bestCandidate;
        }
        finally
        {
            ReturnLevenshteinBuffers(buffers);
        }
    }

    public static string GetSpellingSuggestionForStrings(string name, IEnumerable<string>? candidates)
    {
        // Go 的 string 零值是 ""，而 C# 的无约束泛型在"没找到"时只能给出 default(T)（= null）：
        // 这里在 string 特化的入口把零值补回空串，调用点就能保持 Go 的 `suggestion != ""` 判断。
        return GetSpellingSuggestion(name, candidates, Identity, CompareStringsCaseSensitive) ?? "";
    }

    private sealed class LevenshteinBuffers
    {
        public double[] previous = [];
        public double[] current = [];
    }

    // Go 的 sync.Pool（每线程一份、取用期间独占）。取出时把线程槽位置空：重入调用会另分配一份，
    // 而不是和调用方共用同一对缓冲区（对应 sync.Pool.Get 的独占语义）。
    [ThreadStatic]
    private static LevenshteinBuffers? _tlsLevenshteinBuffers;

    private static LevenshteinBuffers RentLevenshteinBuffers()
    {
        var buffers = _tlsLevenshteinBuffers;
        if (buffers == null)
        {
            return new LevenshteinBuffers();
        }

        _tlsLevenshteinBuffers = null;
        return buffers;
    }

    private static void ReturnLevenshteinBuffers(LevenshteinBuffers buffers)
    {
        _tlsLevenshteinBuffers = buffers;
    }

    private static double LevenshteinWithMax(LevenshteinBuffers buffers, int[] s1, int[] s2, double maxValue)
    {
        var bufferSize = s2.Length + 1;
        buffers.previous = GrowBuffer(buffers.previous, bufferSize);
        buffers.current = GrowBuffer(buffers.current, bufferSize);

        var previous = buffers.previous;
        var current = buffers.current;

        var big = maxValue + 0.01;
        for (var i = 0; i < bufferSize; i++)
        {
            previous[i] = i;
        }

        for (var i = 1; i <= s1.Length; i++)
        {
            var c1 = s1[i - 1];
            var minJ = Math.Max((int)Math.Ceiling(i - maxValue), 1);
            var maxJ = Math.Min((int)Math.Floor(maxValue + i), s2.Length);
            var colMin = (double)i;
            current[0] = colMin;
            for (var j = 1; j < minJ; j++)
            {
                current[j] = big;
            }

            for (var j = minJ; j <= maxJ; j++)
            {
                double substitutionDistance;
                if (ToLowerRune(s1[i - 1]) == ToLowerRune(s2[j - 1]))
                {
                    substitutionDistance = previous[j - 1] + 0.1;
                }
                else
                {
                    substitutionDistance = previous[j - 1] + 2;
                }

                double dist;
                if (c1 == s2[j - 1])
                {
                    dist = previous[j - 1];
                }
                else
                {
                    dist = Math.Min(previous[j] + 1, Math.Min(current[j - 1] + 1, substitutionDistance));
                }

                current[j] = dist;
                colMin = Math.Min(colMin, dist);
            }

            for (var j = maxJ + 1; j <= s2.Length; j++)
            {
                current[j] = big;
            }

            if (colMin > maxValue)
            {
                // Give up -- everything in this column is > max and it can't get better in future columns.
                return -1;
            }

            (previous, current) = (current, previous);
        }

        var res = previous[s2.Length];
        if (res > maxValue)
        {
            return -1;
        }

        return res;
    }

    // Go 的 slices.Grow(buf[:0], size)[:size]：容量够就复用同一底层数组，否则扩到 size（C# 数组长度固定，
    // 所以"够长"直接按数组长度判断，超出 size 的尾部不参与计算）。
    private static double[] GrowBuffer(double[] buffer, int size)
    {
        return buffer.Length >= size ? buffer : new double[size];
    }

    // Go 的 []rune(s)：每个码点一个元素。C# 里用 int 表示码点（docs §3.1）：代理对合成一个码点，
    // 孤立代理项保持其原值（Go 里非法 UTF-8 会解成 U+FFFD，这里能原样保留，两者都是"非法输入"边界）。
    private static int[] ToCodePoints(string s)
    {
        if (s.Length == 0)
        {
            return [];
        }

        var result = new int[s.Length];
        var count = 0;
        for (var i = 0; i < s.Length; i++)
        {
            var ch = s[i];
            if (IsHighSurrogate(ch) && i + 1 < s.Length && IsLowSurrogate(s[i + 1]))
            {
                result[count++] = char.ConvertToUtf32(ch, s[i + 1]);
                i++;
            }
            else
            {
                result[count++] = ch;
            }
        }

        if (count == result.Length)
        {
            return result;
        }

        var trimmed = new int[count];
        Array.Copy(result, trimmed, count);
        return trimmed;
    }

    // Go 的 unicode.ToLower：代理项与越界码点没有映射，原样返回（unicode 表里不存在这些映射）。
    private static int ToLowerRune(int ch)
    {
        if (ch < 0 || ch > 0x10FFFF || IsSurrogate(ch))
        {
            return ch;
        }

        return Rune.ToLowerInvariant(new Rune(ch)).Value;
    }

    public static T Identity<T>(T t)
    {
        return t;
    }

    public static T[] CheckEachDefined<T>(T[]? s, string msg)
        where T : class
    {
        if (s == null)
        {
            return [];
        }

        foreach (var value in s)
        {
            if (value == null)
            {
                Debug.Fail(msg);
            }
        }

        return s;
    }

    public static int IndexAfter(string s, string pattern, int startIndex)
    {
        var matched = s.IndexOf(pattern, startIndex, StringComparison.Ordinal);
        if (matched == -1)
        {
            return -1;
        }

        return matched;
    }

    public static bool ShouldRewriteModuleSpecifier(string specifier, CompilerOptions compilerOptions)
    {
        return compilerOptions.RewriteRelativeImportExtensions.IsTrue() && PathIsRelative(specifier) && !IsDeclarationFileName(specifier) && HasTSFileExtension(specifier);
    }

    public static T[] SingleElementSlice<T>(T? element)
        where T : class
    {
        if (element == null)
        {
            return [];
        }

        return [element];
    }

    public static IEnumerable<T> ConcatenateSeq<T>(params IEnumerable<T>?[] seqs)
    {
        foreach (var seq in seqs)
        {
            if (seq == null)
            {
                continue;
            }

            foreach (var e in seq)
            {
                yield return e;
            }
        }
    }

    // Enumerate returns a sequence of (index, value) pairs from the input sequence.
    public static IEnumerable<(int Index, T Value)> Enumerate<T>(IEnumerable<T>? seq)
    {
        if (seq == null)
        {
            yield break;
        }

        var i = 0;
        foreach (var v in seq)
        {
            yield return (i, v);
            i++;
        }
    }

    private static bool ComparableValuesEqual<T>(T a, T b)
    {
        return EqualityComparer<T>.Default.Equals(a, b);
    }

    // DiffMaps compares two maps m1 and m2 and calls the provided callbacks for added, removed, and changed entries.
    // onAdded is called for each key-value pair that is in m2 but not in m1.
    // onRemoved is called for each key-value pair that is in m1 but not in m2.
    // onChanged is called for each key where the value in m1 differs from the value in m2.
    public static void DiffMaps<K, V>(
        Dictionary<K, V>? m1,
        Dictionary<K, V>? m2,
        Action<K, V>? onAdded,
        Action<K, V>? onRemoved,
        Action<K, V, V>? onChanged)
        where K : notnull
    {
        DiffMapsFunc(m1, m2, (v1, v2) => ComparableValuesEqual(v1, v2), onAdded, onRemoved, onChanged);
    }

    // DiffMapsFunc compares two maps m1 and m2 and calls the provided callbacks for added, removed, and changed entries.
    // onAdded is called for each key-value pair that is in m2 but not in m1.
    // onRemoved is called for each key-value pair that is in m1 but not in m2.
    // onChanged is called for each key where the value in m1 differs from the value in m2.
    public static void DiffMapsFunc<K, V1, V2>(
        Dictionary<K, V1>? m1,
        Dictionary<K, V2>? m2,
        Func<V1, V2, bool> equalValues,
        Action<K, V2>? onAdded,
        Action<K, V1>? onRemoved,
        Action<K, V1, V2>? onChanged)
        where K : notnull
    {
        if (onAdded != null && m2 != null)
        {
            foreach (var (k, v2) in m2)
            {
                if (m1 == null || !m1.ContainsKey(k))
                {
                    onAdded(k, v2);
                }
            }
        }

        if (onChanged == null && onRemoved == null)
        {
            return;
        }

        if (m1 == null)
        {
            return;
        }

        foreach (var (k, v1) in m1)
        {
            if (m2 != null && m2.TryGetValue(k, out var v2))
            {
                if (onChanged != null && !equalValues(v1, v2))
                {
                    onChanged(k, v1, v2);
                }
            }
            else if (onRemoved != null)
            {
                // Go 在这里无条件调用 onRemoved（nil 时会 panic，见文件头 10）；C# 侧跳过 nil。
                onRemoved(k, v1);
            }
        }
    }

    // CopyMapInto is maps.Copy, unless dst is nil, in which case it clones and returns src.
    // Use CopyMapInto anywhere you would use maps.Copy preceded by a nil check and map initialization.
    public static Dictionary<K, V> CopyMapInto<K, V>(Dictionary<K, V>? dst, Dictionary<K, V>? src)
        where K : notnull
    {
        var result = dst ?? [];
        if (src != null)
        {
            foreach (var (k, v) in src)
            {
                result[k] = v;
            }
        }

        return result;
    }

    // UnorderedEqual returns true if s1 and s2 contain the same elements, regardless of order.
    public static bool UnorderedEqual<T>(T[]? s1, T[]? s2)
        where T : notnull
    {
        var len1 = s1?.Length ?? 0;
        var len2 = s2?.Length ?? 0;
        if (len1 != len2)
        {
            return false;
        }

        var counts = new Dictionary<T, int>(len1);
        if (s1 != null)
        {
            foreach (var v in s1)
            {
                counts[v] = counts.GetValueOrDefault(v) + 1;
            }
        }

        if (s2 != null)
        {
            foreach (var v in s2)
            {
                var count = counts.GetValueOrDefault(v) - 1;
                counts[v] = count;
                if (count < 0)
                {
                    return false;
                }
            }
        }

        return true;
    }

    public static T[] Deduplicate<T>(T[]? slice)
    {
        if (slice == null)
        {
            return [];
        }

        if (slice.Length > 1)
        {
            for (var i = 0; i < slice.Length; i++)
            {
                if (Array.IndexOf(slice, slice[i], 0, i) < 0)
                {
                    continue;
                }

                var result = new List<T>(i);
                for (var j = 0; j < i; j++)
                {
                    result.Add(slice[j]);
                }

                for (i++; i < slice.Length; i++)
                {
                    var value = slice[i];
                    if (!result.Contains(value))
                    {
                        result.Add(value);
                    }
                }

                return result.ToArray();
            }
        }

        return slice;
    }

    public static T[] DeduplicateSorted<T>(T[]? slice, Func<T, T, bool> isEqual)
    {
        if (slice == null || slice.Length == 0)
        {
            return slice ?? [];
        }

        // Go 的 `deduplicated := slice[:1]` + append 会就地压缩输入数组（无重复时返回值与输入共享底层数组）；
        // C# 一律用新数组，只在"没有重复"时返回输入本身以保留引用相等的可观察行为（文件头 2/3）。
        var last = slice[0];
        var deduplicated = new List<T> { slice[0] };
        for (var i = 1; i < slice.Length; i++)
        {
            var next = slice[i];
            if (isEqual(last, next))
            {
                continue;
            }

            deduplicated.Add(next);
            last = next;
        }

        if (deduplicated.Count == slice.Length)
        {
            return slice;
        }

        return deduplicated.ToArray();
    }

    // CompareBooleans treats true as greater than false.
    public static int CompareBooleans(bool a, bool b)
    {
        if (a && !b)
        {
            return 1;
        }
        else if (!a && b)
        {
            return -1;
        }

        return 0;
    }
}
