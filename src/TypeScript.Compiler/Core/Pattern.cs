// ported from: internal/core/pattern.go
//
// 与 Go 的差异（依据 docs/architecture.md §3.1，都不改变语义）：
// - Go 的 Pattern 是字段导出、可变的值类型；C# 用结构体 + 公共字段/属性保持同样的值语义与可变性
//   （`Pattern{Text: …, StarIndex: …}` → `new Pattern { Text = …, StarIndex = … }`）。
// - Go 的 string 零值是 ""，而 C# 结构体字段的零值是 null：`TryParsePattern` 在"多个 *"时返回
//   零值 Pattern{}，其 IsValid() 必须为 false、且任何方法都不能因为 Text 为 null 而崩。所以 Text
//   是"可空后备字段 + ?? "" 的 getter"。
// - Go 的 Text 是 UTF-8 字节串，StarIndex 与切片下标都是**字节**下标；C# 里是 UTF-16 代码单元下标。
//   本类型只用于模块名/路径通配（'*' 前缀都是 ASCII），两种口径的前缀/后缀/长度判断结果相同；
//   非 ASCII 前缀时 Go 的 `len(p.Text)-1`（字节）与 C# 的 `Text.Length-1`（代码单元）可能不同，
//   这是 docs §3.1 里"字节下标 → UTF-16 下标"的同一类偏离。
// - Go 的 `panic("candidate does not match pattern")` → Debug.Fail（抛 DebugFailureException，§3.2）。
// - 包级函数（TryParsePattern / FindBestPatternMatch）按 §2 放进同名的静态类 Core；Go 的
//   `T any` 加上"没找到就返回零值"的语义在 C# 里是 `default!`（引用类型即 null，见 TSPath/Path.cs 文件头）。
namespace TypeScript.Core;

public struct Pattern
{
    private string? text;

    /// <summary>Go 的 <c>Pattern.Text</c>（Go 的零值是空串，这里把 null 归一成空串）。</summary>
    public string Text
    {
        get => text ?? "";
        set => text = value;
    }

    public int StarIndex; // -1 for exact match

    public bool IsValid()
    {
        return StarIndex == -1 || StarIndex < Text.Length;
    }

    public bool Matches(string candidate)
    {
        if (StarIndex == -1)
        {
            return Text == candidate;
        }

        return candidate.Length >= Text.Length - 1
            && candidate.StartsWith(Text[..StarIndex], StringComparison.Ordinal)
            && candidate.EndsWith(Text[(StarIndex + 1)..], StringComparison.Ordinal);
    }

    public string MatchedText(string candidate)
    {
        if (!Matches(candidate))
        {
            Debug.Fail("candidate does not match pattern");
        }

        if (StarIndex == -1)
        {
            return "";
        }

        return candidate[StarIndex..(candidate.Length - Text.Length + StarIndex + 1)];
    }
}

public static partial class Core
{
    public static Pattern TryParsePattern(string pattern)
    {
        var starIndex = pattern.IndexOf('*');
        if (starIndex == -1 || pattern.IndexOf('*', starIndex + 1) == -1)
        {
            return new Pattern { Text = pattern, StarIndex = starIndex };
        }

        // Go 返回零值 Pattern{}（Text 为空串、StarIndex 为 0 ⇒ IsValid() 为 false）。
        return default;
    }

    public static T FindBestPatternMatch<T>(T[] values, Func<T, Pattern> getPattern, string candidate)
    {
        T bestPattern = default!;
        var longestMatchPrefixLength = -1;
        foreach (var value in values)
        {
            var pattern = getPattern(value);
            if ((pattern.StarIndex == -1 || pattern.StarIndex > longestMatchPrefixLength) && pattern.Matches(candidate))
            {
                bestPattern = value;
                longestMatchPrefixLength = pattern.StarIndex;
            }
        }

        return bestPattern;
    }
}
