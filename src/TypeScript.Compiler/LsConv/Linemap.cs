// ported from: tsc/internal/ls/lsconv/linemap.go (71 行)
//
// 与 Go 的偏离（逐条）：
// 1) `type LSPLineStarts []core.TextPos`（Go 的切片类型别名）→ 只读结构体包装（照 Core/Text.cs 的
//    `ECMALineStarts` 先例）：Go 的切片是"数组引用 + 长度"的值语义，C# 数组引用也能表达，但显式包装能保住
//    `LSPLineStarts` 这个**导出类型名**（api-parity 检查按类型逐条比对）。两个方向的 implicit 转换让 Go 的
//    `lineStarts[i]` / `len(starts)` 写法照旧。
// 2) 包级函数 `ComputeLSPLineStarts` → 静态类 `LsConv` 的 public static（docs/architecture.md §2：一个
//    Go package = 一个 C# 命名空间 + 同名静态类放包级函数）。C# 未导出函数 → 本文件的 `private static`。
// 3) Go 的 `panic` 在这里不存在（无 panic 分支）。
// 4) **字节 vs 代码单元**：Go 的 `text[pos]`/`utf8.DecodeRuneInString`/`utf8.RuneSelf` 都在**UTF-8 字节**上
//    工作；本仓的内部坐标是 **UTF-16 代码单元**（Core/Text.cs 文件头），所以这里按 UTF-16 重写：
//      * `b < utf8.RuneSelf`（字节 < 0x80，即 ASCII）→ `c < 0x80`；
//      * 非 ASCII 分支只需要"跳过一整个码点"：`Scanner.DecodeRuneAt` 的 `size` 就是消耗的 UTF-16 代码单元数
//        （2 = 代理对，1 = BMP / 孤立代理项），与 Go 的 `size`（UTF-8 字节数）在"跳过一个码点"这个用法上等价。
//    `AsciiOnly` 的语义不变：文本里只要出现一个非 ASCII 码点就是 false。
// 5) `strings.Count(text, "\n")` 只用于容量提示 → 本文件的 `CountChar`。
// 6) `slices.BinarySearchFunc` / `slices.BinarySearch`（Go 1.21 语义：返回"目标可插入的最左位置" + 是否命中）
//    在 Core 里没有等价物（Core 只有 `TryBinarySearchUniqueFunc`）→ 本文件的 `BinarySearchLineStarts`
//    逐条实现同一语义，供 `ComputeIndexOfLineStart` 与 `Converters.PositionToLineAndCharacter` 共用。

using TypeScript.Core;
using Scan = TypeScript.Scanner.Scanner;

namespace TypeScript.LsConv;

/// <summary>
/// Go: <c>type LSPLineStarts []core.TextPos</c>（linemap.go:10）。LSP 行首位置表（只认 <c>\n</c>、
/// <c>\r</c>、<c>\r\n</c> 三种换行，与 ECMA 行首表的差别见 <see cref="LsConv.ComputeLSPLineStarts"/>）。
/// </summary>
public readonly struct LSPLineStarts(TextPos[] items)
{
    public readonly TextPos[] Items = items;

    public int Length => Items.Length;

    public TextPos this[int index] => Items[index];

    public static implicit operator TextPos[](LSPLineStarts value) => value.Items;

    public static implicit operator LSPLineStarts(TextPos[] items) => new(items);

    public IEnumerator<TextPos> GetEnumerator()
    {
        // foreach 走模式匹配的 GetEnumerator（不经过隐式转换），所以要自己给出（同 Core.ECMALineStarts）。
        foreach (var item in Items)
        {
            yield return item;
        }
    }
}

/// <summary>Go: <c>type LSPLineMap struct</c>（linemap.go:12）。</summary>
public sealed class LSPLineMap
{
    public LSPLineStarts LineStarts;

    /// <summary>Go 的 <c>AsciiOnly bool</c>：整份文本是否只有 ASCII（决定行/列换算能否走常数时间快路径）。</summary>
    public bool AsciiOnly;

    /// <summary>
    /// Go: <c>func (lm *LSPLineMap) ComputeIndexOfLineStart(targetPos core.TextPos) int</c>（linemap.go:56-71，
    /// 移植自 TS 的 <c>computeLineOfPosition(lineStarts, position, lowerBound)</c>）。
    /// </summary>
    public int ComputeIndexOfLineStart(TextPos targetPos)
    {
        var (lineNumber, found) = LsConv.BinarySearchLineStarts(LineStarts.Items, targetPos);
        if (!found && lineNumber > 0)
        {
            // 没命中时二分返回的是"目标若在表里会被插入的位置"（例如行首是 [5,10,23,80] 而目标 20 → (3,false)），
            // 我们要的是**前一个**行首的下标，所以减一。
            lineNumber--;
        }

        return lineNumber;
    }
}

public static partial class LsConv
{
    /// <summary>
    /// Go: <c>func ComputeLSPLineStarts(text string) *LSPLineMap</c>（linemap.go:16-53）。
    /// 与 <c>core.ComputeLineStarts</c> 类似，但只把 <c>\n</c>/<c>\r</c>/<c>\r\n</c> 当作换行，并顺带报告文本是否纯 ASCII。
    /// </summary>
    public static LSPLineMap ComputeLSPLineStarts(string text)
    {
        var lineStarts = new List<TextPos>(CountChar(text, '\n') + 1);
        var asciiOnly = true;

        var textLen = text.Length;
        var pos = 0;
        var lineStart = 0;
        while (pos < textLen)
        {
            var c = text[pos];
            if (c < 0x80)
            {
                pos++;
                if (c == '\r')
                {
                    if (pos < textLen && text[pos] == '\n')
                    {
                        pos++;
                    }
                }
                if (c == '\r' || c == '\n')
                {
                    lineStarts.Add(lineStart);
                    lineStart = pos;
                }
            }
            else
            {
                Scan.DecodeRuneAt(text, pos, out var size);
                pos += size;
                asciiOnly = false;
            }
        }

        lineStarts.Add(lineStart);

        return new LSPLineMap
        {
            LineStarts = lineStarts.ToArray(),
            AsciiOnly = asciiOnly,
        };
    }

    /// <summary>Go 里 `strings.Count(text, "\n")`（只作容量提示）。</summary>
    private static int CountChar(string text, char c)
    {
        var count = 0;
        foreach (var ch in text)
        {
            if (ch == c)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Go 的 <c>slices.BinarySearch</c>/<c>slices.BinarySearchFunc</c> 语义：返回目标可插入的最左下标，
    /// 以及该处是否恰好等于目标。
    /// </summary>
    internal static (int Index, bool Found) BinarySearchLineStarts(TextPos[] lineStarts, TextPos target)
    {
        var lo = 0;
        var hi = lineStarts.Length;
        while (lo < hi)
        {
            var mid = (lo + hi) >> 1;
            if (lineStarts[mid] < target)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return (lo, lo < lineStarts.Length && lineStarts[lo] == target);
    }
}
