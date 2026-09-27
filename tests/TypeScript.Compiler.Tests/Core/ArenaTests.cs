// 新增测试：Go 侧 **没有** arena_test.go，这是本移植额外加的最小守门测试（父 agent 已确认 Arena<T> 是
// struct 场景的稳定槽位来源，且 C# 侧的实现机理与 Go 不同，必须有测试钉住）。
//
// 为什么必须有：C# 里 New() 的"元素地址稳定"靠两条保证——(1) 扩容时**废弃**旧数组而不是就地搬移/拷贝元素，
// (2) 返回的是托管内部 byref（GC 会在压缩搬移时更新它）。若以后有人把 T[] 换成 List<T>、或改成
// Array.Resize 就地扩容，长期持有 `ref T` 的调用点会静默失效，而 Go 侧没有测试能发现。
//
// 只断言契约级行为（不测内部块布局）：
//   1) New() 的槽位是零值且互不别名；
//   2) New() 触发多次扩容后，先前拿到的引用仍指向同一元素；
//   3) NewSlice / NewSlice1 / Clone 的长度、内容、互不覆盖（它们已退化为普通分配，见 Arena.cs 文件头）；
//   4) nextArenaSize 的扩容序列（internal；测试程序集有 InternalsVisibleTo，见 Properties/AssemblyInfo.cs）。
using TypeScript.Core;
using Xunit;
using static TypeScript.Core.Core;

namespace TypeScript.Tests;

public class CoreArenaTests
{
    [Fact]
    public void TestArenaNewReturnsZeroedDistinctSlots()
    {
        var arena = new Arena<long>();

        ref var first = ref arena.New();
        Assert.Equal(0L, first); // 新槽位是零值（Go 的 arena 块是新分配的零值内存）
        first = 1;

        ref var second = ref arena.New();
        second = 2;

        // 若两个槽位别名，上面一行会把 first 覆盖成 2
        Assert.Equal(1L, first);
        Assert.Equal(2L, second);
    }

    [Fact]
    public void TestArenaNewReferencesSurviveGrowth()
    {
        var arena = new Arena<long>();

        ref var first = ref arena.New();
        first = 42;
        ref var second = ref arena.New();
        second = 7;

        // 触发多次扩容（块大小 2,4,8,...,256：300 次分配一定跨过多块）
        for (var i = 0; i < 300; i++)
        {
            ref var x = ref arena.New();
            x = i;
        }

        // 旧块被废弃（不拷贝、不搬移）后，先前返回的引用仍指向原来的元素
        Assert.Equal(42L, first);
        Assert.Equal(7L, second);
    }

    [Fact]
    public void TestArenaNewSlice()
    {
        var arena = new Arena<int>();

        // Go: size == 0 → nil 切片；C# → 空数组
        Assert.Empty(arena.NewSlice(0));

        // 长度 = size、元素是零值
        var slice = arena.NewSlice(3);
        Assert.Equal(3, slice.Length);
        Assert.Equal(0, slice[2]);
        slice[0] = 1;
        slice[2] = 3;
        Assert.Equal(1, slice[0]);
        Assert.Equal(3, slice[2]);

        // 每次请求都是独立的一段（Go 的三下标切片 `data[i:j:j]` 也是"长度=容量=size 的独立区域"）：
        // 随后的 New() / NewSlice() 不会覆盖已经返回的切片
        ref var next = ref arena.New();
        next = 99;
        var other = arena.NewSlice(3);
        other[0] = -1;
        Assert.Equal(1, slice[0]);
        Assert.Equal(3, slice[2]);
        Assert.Equal(99, next);
        Assert.Equal(-1, other[0]);
    }

    [Fact]
    public void TestArenaNewSlice1AndClone()
    {
        var arena = new Arena<int>();

        var one = arena.NewSlice1(42);
        Assert.Equal(42, Assert.Single(one)); // xUnit2013：长度 1 用 Assert.Single（同时覆盖 slice[0]）

        var source = new[] { 9, 8, 7 };
        var clone = arena.Clone(source);
        Assert.Equal(3, clone.Length);
        Assert.Equal(9, clone[0]);
        Assert.Equal(8, clone[1]);
        Assert.Equal(7, clone[2]);

        // 克隆与源互不影响
        source[0] = -1;
        Assert.Equal(9, clone[0]);
        clone[1] = -2;
        Assert.Equal(8, source[1]);

        // 空输入 → 空数组（Go 的 nil 切片）
        Assert.Empty(arena.Clone(Array.Empty<int>()));
    }

    [Fact]
    public void TestArenaNextArenaSize()
    {
        // Go: size = max(size, 1); size = min(size*2, 256)
        Assert.Equal(2, NextArenaSize(0));
        Assert.Equal(2, NextArenaSize(1));
        Assert.Equal(4, NextArenaSize(2));
        Assert.Equal(8, NextArenaSize(4));
        Assert.Equal(256, NextArenaSize(128));
        Assert.Equal(256, NextArenaSize(200));
        Assert.Equal(256, NextArenaSize(256));
    }
}
