// ported from: internal/ls/lsconv/linemap.go 的行为（Go 侧没有 linemap_test.go，本文件是**新增**的确定性用例）
//
// 与 Go 的差异：
// - Go 的 `LSPLineStarts []core.TextPos` 在 C# 里是只读结构体包装（见 Linemap.cs 文件头第 1 条），
//   断言时用 `LineStarts.Items`（或直接当 `TextPos[]` 用）。
// - Go 侧没有对应测试：这里补上，因为 `ComputeLSPLineStarts`/`ComputeIndexOfLineStart` 是 LSP 行列换算的地基，
//   且行尾规则（只认 \n / \r / \r\n）与 core 的 ECMA 行首表不同，容易改坏。
using TypeScript.Core;
using TypeScript.LsConv;
using Xunit;

using static TypeScript.LsConv.LsConv;

namespace TypeScript.Tests;

public class LinemapTests
{
    [Theory]
    [InlineData("", new int[] { 0 }, true)]
    [InlineData("abc", new int[] { 0 }, true)]
    [InlineData("a\nb", new int[] { 0, 2 }, true)]
    [InlineData("a\r\nb", new int[] { 0, 3 }, true)]
    [InlineData("a\rb", new int[] { 0, 2 }, true)]
    [InlineData("a\r\nb\rc\nd", new int[] { 0, 3, 5, 7 }, true)]
    [InlineData("\n\n\r\n\r", new int[] { 0, 1, 2, 4, 5 }, true)]
    [InlineData("a\n", new int[] { 0, 2 }, true)]
    [InlineData("\u03b1\n\u03b2", new int[] { 0, 2 }, false)]
    [InlineData("\U0001F600\r\nx", new int[] { 0, 4 }, false)]
    public void ComputeLSPLineStartsHandlesLspLineBreaks(string text, int[] expectedLineStarts, bool expectedAsciiOnly)
    {
        var lineMap = ComputeLSPLineStarts(text);

        Assert.Equal(expectedLineStarts, lineMap.LineStarts.Items.Select(static p => p.Value));
        Assert.Equal(expectedAsciiOnly, lineMap.AsciiOnly);
    }

    [Fact]
    public void ComputeLSPLineStartsTreatsNonBmpAsOneAsciiBreak()
    {
        // 代理对只算一个码点：AsciiOnly=false，且换行位置按 UTF-16 代码单元计（😀 = 2 个单元）。
        var text = "\U0001F600\U0001F600\nz";
        var lineMap = ComputeLSPLineStarts(text);

        Assert.False(lineMap.AsciiOnly);
        Assert.Equal(new[] { 0, 5 }, lineMap.LineStarts.Items.Select(static p => p.Value));
    }

    [Theory]
    // 行首表 [0, 3, 5, 7] 对应 "a\r\n" / "b\r" / "c\n" / "d"
    [InlineData(0, 0)] // 命中最左行首
    [InlineData(1, 0)] // 行内
    [InlineData(2, 0)] // \n（属于第 0 行）
    [InlineData(3, 1)] // 命中的行首
    [InlineData(4, 1)]
    [InlineData(5, 2)]
    [InlineData(7, 3)]
    [InlineData(8, 3)] // 文本末尾
    public void ComputeIndexOfLineStartMatchesBinarySearchSemantics(int targetPos, int expectedLine)
    {
        var lineMap = ComputeLSPLineStarts("a\r\nb\rc\nd");

        Assert.Equal(expectedLine, lineMap.ComputeIndexOfLineStart(new TextPos(targetPos)));
    }
}
