// ported from: internal/sourcemap/util.go
//
// 与 Go 的差异（逐条）：
// - `strings.TrimLeftFunc(line, unicode.IsSpace)` / `TrimRightFunc(...)` → C# 的 `char.IsWhiteSpace` 逐 char 裁剪。
//   两者都是 Unicode White_Space；唯一差别是 .NET 还把 U+001C…U+001F 当空白而 Go 的 unicode.IsSpace 不当
//   （源码里出现的概率为零，但记录在此）。
// - `stringutil.IsLineBreak`（\n \r U+2028 U+2029，全在 BMP）→ 逐 char 裁剪（Go 的 rune 语义等价）。
// - `strings.CutPrefix` → `StartsWith` + 切片。
// - 包级函数 `TryGetSourceMappingURL` → 静态类 `SourceMap` 上的方法（docs/architecture.md §2）。
namespace TypeScript.SourceMap;

using static TypeScript.StringUtil.StringUtil;

public static partial class SourceMap
{
    /// <summary>Tries to find the sourceMappingURL comment at the end of a file.</summary>
    public static string TryGetSourceMappingURL(ECMALineInfo? lineInfo)
    {
        if (lineInfo is not null)
        {
            for (var index = lineInfo.LineCount() - 1; index >= 0; index--)
            {
                var line = TrimLeftSpace(lineInfo.LineText(index));
                line = TrimRightLineBreak(line);
                if (line.Length == 0)
                {
                    continue;
                }

                if (line.Length < 4 || !line.StartsWith("//", StringComparison.Ordinal) ||
                    (line[2] != '#' && line[2] != '@') || line[3] != ' ')
                {
                    break;
                }

                var rest = line[4..];
                if (rest.StartsWith("sourceMappingURL=", StringComparison.Ordinal))
                {
                    return TrimRightSpace(rest["sourceMappingURL=".Length..]);
                }
            }
        }

        return "";
    }

    internal static string TrimLeftSpace(string s)
    {
        var i = 0;
        while (i < s.Length && char.IsWhiteSpace(s[i]))
        {
            i++;
        }

        return s[i..];
    }

    internal static string TrimRightSpace(string s)
    {
        var end = s.Length;
        while (end > 0 && char.IsWhiteSpace(s[end - 1]))
        {
            end--;
        }

        return s[..end];
    }

    internal static string TrimRightLineBreak(string s)
    {
        var end = s.Length;
        while (end > 0 && IsLineBreak(s[end - 1]))
        {
            end--;
        }

        return s[..end];
    }
}
