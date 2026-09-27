// ported from: internal/stringutil/identifier.go
//
// 偏离 Go 的地方：参数用 int 而不是 System.Text.Rune。Go 的 rune 是 int32，unicode.Is 的判定域是整个
// rune 值域（含 ID_Start/ID_Continue 里的 astral 码点、以及必须返回 false 的孤立代理项）；C# 的
// System.Text.Rune 无法表示代理项，char 又会在 astral 码点上静默截断，所以 int 才是 rune 的精确对应物
// （详见 Util.cs 文件头）。调用点从 Rune 传参时写 ch.Value 即可。
namespace TypeScript.StringUtil;

public static partial class StringUtil
{
    /// <summary>
    /// IsUnicodeIdentifierStart reports whether ch may begin an ECMAScript
    /// identifier, i.e. whether it has the Unicode ID_Start (or Other_ID_Start)
    /// property. The range table is generated; see generate-unicode-data.mts.
    /// </summary>
    public static bool IsUnicodeIdentifierStart(int ch)
    {
        return UnicodeESNextIdentifierStart.Contains(ch);
    }

    /// <summary>
    /// IsUnicodeIdentifierPart reports whether ch may appear after the first
    /// character of an ECMAScript identifier, i.e. whether it has the Unicode
    /// ID_Continue (or Other_ID_Continue) property, which also includes ID_Start.
    /// </summary>
    public static bool IsUnicodeIdentifierPart(int ch)
    {
        return UnicodeESNextIdentifierPart.Contains(ch);
    }
}
