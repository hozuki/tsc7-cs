// ported from: internal/collections/ordered_set_test.go
//
// 与 Go 测试的差异：
// - TestOrderedSetWithSizeHint（ordered_set_test.go:40-53）在 Go 里断言 testing.AllocsPerRun 数出来的
//   **分配次数** < 10；C# 没有该 API（只有分配字节数），阈值无法 1:1 搬运，这里退化为功能检查
//   （用尺寸提示构造 + 塞满 N 个元素 + 验证，覆盖 NewOrderedSetWithSizeHint 这条构造路径）。
// - Go 的 `s := &collections.OrderedSet[int]{}` → C# 的 `new OrderedSet<int>()`。
// - Go 的 assert.Equal(t, len(values), 2) → Assert.Equal(2, values.Length)。
// - Go 只断言 `slices.IsSorted(values)`；这里直接断言插入顺序 [1, 3]（更强且确定）。
using TypeScript.Collections;
using Xunit;
using static TypeScript.Collections.Collections;

namespace TypeScript.Tests;

public class CollectionsOrderedSetTests
{
    [Fact]
    public void TestOrderedSet()
    {
        var s = new OrderedSet<int>();

        s.Add(1);
        s.Add(2);
        s.Add(3);

        Assert.True(s.Has(1));
        Assert.True(s.Has(2));
        Assert.True(s.Has(3));

        Assert.True(s.Delete(2));

        var values = s.Values().ToArray(); // Go: slices.Collect(s.Values())
        Assert.Equal(2, values.Length);
        Assert.Equal(new[] { 1, 3 }, values);

        s.Clear();

        Assert.Equal(0, s.Size());
        Assert.False(s.Has(1));
        Assert.False(s.Has(2));
        Assert.False(s.Has(3));

        var s2 = s.Clone();
        Assert.NotSame(s, s2);
        Assert.Equal(0, s2.Size());
    }

    // Go: TestOrderedSetWithSizeHint（断言被替换成功能检查，见文件头）。
    [Fact]
    public void TestOrderedSetWithSizeHint()
    {
        const int N = 1024;

        var s = NewOrderedSetWithSizeHint<int>(N);
        for (var i = 0; i < N; i++)
        {
            s.Add(i);
        }

        Assert.Equal(N, s.Size());
    }
}
