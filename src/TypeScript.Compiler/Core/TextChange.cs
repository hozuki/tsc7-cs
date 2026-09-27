// ported from: internal/core/textchange.go
//
// C# 侧的偏离（逐条）：
// 1) Go 内嵌 `TextRange`（字段提升出 Pos()/End()）→ C# 里 TextRange 是值类型字段 `Range`，
//    调用点写 `change.Range.Pos()`（与 spanmap/scanner 里 CommentRange 的处理一致）。
// 2) `ApplyTo` 的 `text[:t.Pos()] + t.NewText + text[t.End():]` 是 Go 的字符串拼接（两次切片 + 拼接）；
//    C# 用 `string.Create` 一次分配写完（§3.8：避免中间字符串）。
// 3) `ApplyBulkEdits` 的 `strings.Builder` + `Grow` → `string.Create` + `CopyTo`（同样零中间分配）；
//    Go 的 `text[lastEnd:...]` 切片视图在 C# 里用 `AsSpan`。
using System.Text;

namespace TypeScript.Core;

/// <summary>Go: <c>type TextChange struct { TextRange; NewText string }</c>。</summary>
public readonly struct TextChange(TextRange range, string newText)
{
    public readonly TextRange Range = range;

    public readonly string NewText = newText;

    /// <summary>Go: <c>func (t TextChange) ApplyTo(text string) string</c>。</summary>
    public string ApplyTo(string text)
    {
        var pos = Range.Pos();
        var end = Range.End();
        return string.Create(
            pos + NewText.Length + (text.Length - end),
            (text, NewText, pos, end),
            static (span, state) =>
            {
                var (source, newText, start, stop) = state;
                source.AsSpan(0, start).CopyTo(span);
                newText.AsSpan().CopyTo(span[start..]);
                source.AsSpan(stop).CopyTo(span[(start + newText.Length)..]);
            });
    }
}

public static partial class Core
{
    /// <summary>Go: <c>func ApplyBulkEdits(text string, edits []TextChange) string</c>。</summary>
    public static string ApplyBulkEdits(string text, ReadOnlySpan<TextChange> edits)
    {
        var builder = new StringBuilder(text.Length);
        var lastEnd = 0;
        foreach (var edit in edits)
        {
            var start = edit.Range.Pos();
            if (start != lastEnd)
            {
                builder.Append(text.AsSpan(lastEnd, start - lastEnd));
            }

            builder.Append(edit.NewText);
            lastEnd = edit.Range.End();
        }

        builder.Append(text.AsSpan(lastEnd));
        return builder.ToString();
    }
}
