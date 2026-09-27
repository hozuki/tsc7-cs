// ported from: internal/ast/positionmap_test.go
//
// **重要偏离（必须先读）**：Go 的 PositionMap 做的是 "UTF-8 字节偏移 ↔ UTF-16 代码单元偏移" 的换算，因为 Go 的 string
// 是 UTF-8 字节序列；C# 的 string 本身就是 UTF-16 代码单元序列（与 JS 字符串同构），位置就是 UTF-16 下标，两种偏移
// **恒等**（依据 docs/architecture.md §3.1，实现见 PositionMap.cs 文件头）。所以：
//   - Go 里"é 之后 delta = 1""🎉 之后 delta = 2"这类**字节 vs 代码单元**的期望在 C# 里没有对应语义，
//     本文件保留同名测试与 Go 的原始期望值（注释里），断言改成 C# 的契约：任意文本上双向都是恒等映射、
//     IsAsciiOnly() 恒为 true（这两条正是 C# 实现要提供的可观察行为，防止以后有人把字节语义搬回来）。
//   - `TestPositionMapLoneSurrogateSentinel` 依赖 `stringutil.EncodeJSStringRune`（§3.1 明确不移植哨兵机制），
//     改成直接放一个孤立代理项字符（C# 字符串允许）。
//   - Go 的 5 个 Benchmark* 不移植（C# 侧没有与 Go benchmark 对应的基础设施；它们只是计时，没有断言）。
using TypeScript.Ast;
using Xunit;

using static TypeScript.Ast.Ast;

namespace TypeScript.Tests;

public class PositionMapTests
{
    [Fact]
    public void TestPositionMapASCII()
    {
        var text = "const x = 1;";
        var pm = ComputePositionMap(text);
        Assert.True(pm.IsAsciiOnly());
        for (var i = 0; i <= text.Length; i++)
        {
            Assert.Equal(i, pm.UTF8ToUTF16(i));
            Assert.Equal(i, pm.UTF16ToUTF8(i));
        }
    }

    [Fact]
    public void TestPositionMapTwoByte()
    {
        // "café" — é (U+00E9) 在 Go 里是 2 字节 UTF-8、1 个 UTF-16 代码单元（delta=1）；
        // C# 里两边都是代码单元下标，所以恒等。
        var text = "const café = 1;\nconst x = 2;";
        var pm = ComputePositionMap(text);

        // Go: 非 ASCII 时 IsAsciiOnly() == false；C# 的 PositionMap 语义与"是否 ASCII"无关，恒为 true（见文件头）。
        Assert.True(pm.IsAsciiOnly());

        for (var i = 0; i <= text.Length; i++)
        {
            Assert.Equal(i, pm.UTF8ToUTF16(i));
            Assert.Equal(i, pm.UTF16ToUTF8(i));
        }
    }

    [Fact]
    public void TestPositionMapFourByte()
    {
        // 🎉 (U+1F389) 在 Go 里是 4 字节 UTF-8、2 个代码单元（delta=2）；C# 里恒等。
        var text = "const a = \"🎉\";\nconst b = 2;";
        var pm = ComputePositionMap(text);
        Assert.True(pm.IsAsciiOnly());

        for (var i = 0; i <= text.Length; i++)
        {
            Assert.Equal(i, pm.UTF8ToUTF16(i));
            Assert.Equal(i, pm.UTF16ToUTF8(i));
        }
    }

    [Fact]
    public void TestPositionMapMultipleNonASCII()
    {
        // "à" (U+00E0) 与 "🎉" (U+1F389) 混排。
        // Go 的期望：{0,0}, {2,1}, {6,3}, {7,4}；C# 的期望：全部恒等（下表保留 Go 的字节偏移作为对照）。
        var text = "à🎉x";
        var pm = ComputePositionMap(text);

        foreach (var utf8 in new[] { 0, 2, 6, 7 })
        {
            Assert.Equal(utf8, pm.UTF8ToUTF16(utf8));
        }

        for (var i = 0; i <= text.Length; i++)
        {
            Assert.Equal(i, pm.UTF16ToUTF8(i));
        }
    }

    [Fact]
    public void TestPositionMapLoneSurrogateSentinel()
    {
        // Go: "a" + stringutil.EncodeJSStringRune(0xD800) + "b"（哨兵字节编码）；C# 直接放孤立代理项 U+D800。
        var text = "a\uD800b";
        var pm = ComputePositionMap(text);
        Assert.True(pm.IsAsciiOnly());

        // Go: UTF8ToUTF16(len(text)) == 3、UTF16ToUTF8(2) == len(text)-1；C# 恒等。
        Assert.Equal(text.Length, pm.UTF8ToUTF16(text.Length));
        Assert.Equal(2, pm.UTF16ToUTF8(2));
    }

    [Fact]
    public void TestPositionMapRoundtrip()
    {
        var text = "let café = \"🎉\"; // naïve";
        var pm = ComputePositionMap(text);

        // Go: 把每个合法 UTF-16 位置转 UTF-8 再转回来；C# 里恒等，同样要往返一致。
        var utf16Len = pm.UTF8ToUTF16(text.Length);
        for (var i = 0; i <= utf16Len; i++)
        {
            var utf8Pos = pm.UTF16ToUTF8(i);
            Assert.Equal(i, pm.UTF8ToUTF16(utf8Pos));
        }
    }
}
