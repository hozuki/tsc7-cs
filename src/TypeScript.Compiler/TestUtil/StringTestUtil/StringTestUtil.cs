// ported from: internal/testutil/stringtestutil/stringtestutil.go
//
// 与 Go 的差异（逐条）：
// 1) `strings.Split(text, "\n")` → 同名的 C# `Split('\n')`（注意：**不是** `StringUtil.SplitLines`，Go 这里是按 `\n` 切）。
// 2) `strings.IndexFunc(line, f)` → 手写循环（返回 -1 表示没找到，与 Go 一致）。
// 3) `strings.ReplaceAll(prefix, "\t", "    ")` → `Replace("\t", "    ", StringComparison.Ordinal)`。
// 4) `strings.TrimSpace` → `Trim()`；`strings.Join(lines, "\n")` → `Join('\n', lines)`。
// 5) Go 的 `lines[startLine : lastLine+1]` 在"整段全是空行"时会越界 panic（startLine = -1）；
//    C# 用数组切片表达同样的算式，同样会抛（ArgumentOutOfRangeException），行为对齐。

namespace TypeScript.TestUtil.StringTestUtil;

using TypeScript.StringUtil;

/// <summary>Go: <c>package stringtestutil</c>。</summary>
public static class StringTestUtil
{
    /// <summary>Go: <c>func Dedent(text string) string</c>。</summary>
    public static string Dedent(string text)
    {
        var lines = text.Split('\n');

        // Remove blank lines in the beginning and end
        // and convert all tabs in the beginning of line to spaces
        var startLine = -1;
        var lastLine = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var firstNonWhite = IndexFunc(line, r => !StringUtil.IsWhiteSpaceLike(r));
            if (firstNonWhite > 0)
            {
                line = line[..firstNonWhite].Replace("\t", "    ", StringComparison.Ordinal) + line[firstNonWhite..];
                lines[i] = line;
            }

            line = line.Trim();
            if (line != "")
            {
                if (startLine == -1)
                {
                    startLine = i;
                }

                lastLine = i;
            }
        }

        var sliced = lines[startLine..(lastLine + 1)];
        var mappedLines = new string[sliced.Length];
        for (var i = 0; i < sliced.Length; i++)
        {
            var trimmed = sliced[i].Trim();
            mappedLines[i] = trimmed == "" ? "" : sliced[i];
        }

        var indentation = StringUtil.GuessIndentation(mappedLines);
        if (indentation > 0)
        {
            for (var i = 0; i < sliced.Length; i++)
            {
                sliced[i] = sliced[i].Length > indentation ? sliced[i][indentation..] : "";
            }
        }

        return string.Join('\n', sliced);
    }

    /// <summary>Go: <c>strings.IndexFunc(line, f)</c>。</summary>
    private static int IndexFunc(string line, Func<char, bool> predicate)
    {
        for (var i = 0; i < line.Length; i++)
        {
            if (predicate(line[i]))
            {
                return i;
            }
        }

        return -1;
    }
}
