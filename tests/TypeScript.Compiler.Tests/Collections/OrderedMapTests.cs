// ported from: internal/collections/ordered_map_test.go
//
// 与 Go 测试的差异（逐条说明）：
//
// - **未移植** TestOrderedMapUnmarshalJSON（ordered_map_test.go:104-129）：它依赖尚未移植的
//   OrderedMap.UnmarshalJSONFrom / TypeScript.Json，等 Json 包（M2）落地后补。
// - TestOrderedMapWithSizeHint（ordered_map_test.go:90-101）在 Go 里断言 testing.AllocsPerRun 数出来的
//   **分配次数** < 10；C# 没有"分配次数"这个 API（只有 GC.GetAllocatedBytesForCurrentThread 的字节数），
//   阈值无法 1:1 搬运，这里退化为功能检查：用尺寸提示构造、塞满 N 个元素、验证结果（覆盖
//   NewOrderedMapWithSizeHint 这条构造路径）。
// - Go 的 `assert.DeepEqual(t, slices.Collect(m.Keys()), []int{...})` → xunit 的
//   `Assert.Equal(new[] {...}, m.Keys())`（xunit 对 IEnumerable 逐元素、按顺序比较，顺序断言完整保留）。
// - Go 的 `var m collections.OrderedMap[int, string]`（零值结构体）与 `&collections.OrderedMap[int, string]{}`
//   都对应 C# 的 `new OrderedMap<int, string>()`。
// - Go 的 `fmt.Sprintf("%10d", n)`（宽度 10、空格左填充）→ PadInt 用 `ToString().PadLeft(10)`。
// - Go 的 string 零值是 ""，C# 的 default(string) 是 null：Get/Delete 未命中时 Go 断言 `v == ""`，
//   这里写成 `Assert.Equal("", v ?? "")`，两者表达的都是"返回 V 的零值"。
using System.Globalization;
using TypeScript.Collections;
using Xunit;
using static TypeScript.Collections.Collections;

namespace TypeScript.Tests;

public class CollectionsOrderedMapTests
{
    [Fact]
    public void TestOrderedMap()
    {
        var m = new OrderedMap<int, string>();

        Assert.False(m.Has(1));

        const int N = 1000;
        const int start = 1;
        const int end = start + N;

        // Seed the map with ascending keys and values for easier testing.
        for (var i = start; i < end; i++)
        {
            m.Set(i, PadInt(i));
        }

        Assert.Equal(N, m.Size());

        // Attempt to overwrite existing keys in reverse order.
        for (var i = end - 1; i >= start; i--)
        {
            m.Set(i, PadInt(i));
        }

        Assert.Equal(N, m.Size());

        for (var i = start; i < end; i++)
        {
            var (v, ok) = m.Get(i);
            Assert.True(ok);
            Assert.Equal(PadInt(i), v);
        }

        foreach (var (k, v) in m.Entries())
        {
            Assert.Equal(PadInt(k), v);
        }

        var keys = m.Keys().ToArray(); // Go: slices.Collect(m.Keys())
        Assert.Equal(N, keys.Length);
        Assert.True(IsSorted(keys)); // Go: slices.IsSorted(keys)

        var values = m.Values().ToArray(); // Go: slices.Collect(m.Values())
        Assert.Equal(N, values.Length);
        Assert.True(IsSorted(values));

        var firstKey = 0;
        foreach (var k in m.Keys())
        {
            firstKey = k;
            break;
        }

        Assert.Equal(start, firstKey);

        string? firstValue = null;
        foreach (var v in m.Values())
        {
            firstValue = v;
            break;
        }

        Assert.Equal(PadInt(start), firstValue);

        foreach (var (k, v) in m.Entries())
        {
            firstKey = k;
            firstValue = v;
            break;
        }

        Assert.Equal(start, firstKey);
        Assert.Equal(PadInt(start), firstValue);

        for (var i = start + 1; i < end; i++)
        {
            var (v, ok) = m.Delete(i);
            Assert.True(ok);
            Assert.Equal(PadInt(i), v);
            Assert.False(m.Has(i));

            (v, ok) = m.Get(i);
            Assert.False(ok);
            Assert.Equal("", v ?? "");

            (v, ok) = m.Delete(i);
            Assert.False(ok);
            Assert.Equal("", v ?? "");
        }

        Assert.Equal(1, m.Size());
        Assert.True(m.Has(start));

        var (lastValue, lastOk) = m.Delete(start);
        Assert.True(lastOk);
        Assert.Equal(PadInt(start), lastValue);

        Assert.Equal(0, m.Size());
    }

    [Fact]
    public void TestOrderedMapClone()
    {
        var m = new OrderedMap<int, string>();
        m.Set(1, "one");
        m.Set(2, "two");

        var clone = m.Clone();

        Assert.NotSame(m, clone);
        Assert.Equal(2, clone.Size());
        Assert.Equal(new[] { 1, 2 }, clone.Keys());
        // Values() 的元素类型是 string?（Go 的 V 零值语义），期望值也用 string?[] 避免可空推断歧义。
        Assert.Equal(new string?[] { "one", "two" }, clone.Values());

        var (v, ok) = clone.Get(1);
        Assert.True(ok);
        Assert.Equal("one", v);

        m.Delete(1);

        Assert.Equal(1, m.Size());
        Assert.Equal(2, clone.Size());
        Assert.Equal(new[] { 1, 2 }, clone.Keys());
        Assert.Equal(new string?[] { "one", "two" }, clone.Values());
    }

    [Fact]
    public void TestOrderedMapClear()
    {
        var m = new OrderedMap<int, string>();
        m.Set(1, "one");
        m.Set(2, "two");

        m.Clear();

        Assert.Equal(0, m.Size());
    }

    // Go: TestOrderedMapWithSizeHint（断言被替换成功能检查，见文件头）。
    [Fact]
    public void TestOrderedMapWithSizeHint()
    {
        const int N = 1024;

        var m = NewOrderedMapWithSizeHint<int, int>(N);
        for (var i = 0; i < N; i++)
        {
            m.Set(i, i);
        }

        Assert.Equal(N, m.Size());
    }

    // Go: slices.IsSorted（用默认比较器，等价于 Go 的 `<`）。
    private static bool IsSorted<T>(T[] values)
    {
        var comparer = Comparer<T>.Default;
        for (var i = 1; i < values.Length; i++)
        {
            if (comparer.Compare(values[i - 1], values[i]) > 0)
            {
                return false;
            }
        }

        return true;
    }

    // Go: fmt.Sprintf("%10d", n)
    private static string PadInt(int n)
    {
        return n.ToString(CultureInfo.InvariantCulture).PadLeft(10);
    }
}
