// ported from: internal/locale/locale.go
//
// C# 映射（Go 的 golang.org/x/text/language.Tag → C# 的规范化 BCP-47 标签字符串）：
//
//   Go                                   C#
//   type Locale language.Tag             public readonly struct Locale（私有 string? 字段存规范化后的标签）
//   var Default Locale                   public static readonly Locale Default（= default(Locale)，即"未指定"）
//   func (l Locale) String() string      public string String()（保留 Go 的方法名；同时覆写 ToString()，
//                                        这样 $"{locale}" / string.Format 也得到同样的文本，见 §3.3）
//   func Parse(s string) (Locale, bool)  public static bool TryParse(string, out Locale)（§3.2：(T, bool) → TryX）
//   func WithLocale(ctx, l) context.Context  public static Context WithLocale(Context, Locale)
//   func FromContext(ctx) Locale         public static Locale FromContext(Context)
//   func HasLocale(ctx) bool             public static bool HasLocale(Context)
//
// context 的映射（Go 的 context.WithValue(contextKey(0), locale)）：C# 侧没有"任意键值 context"，
// 按 docs/architecture.md §2 的既定做法把 locale 直接挂到 TypeScript.Core.Context 上（Context.cs 的头注释
// 也是这么预告的）：`Locale.WithLocale(ctx, loc)` 生成一个新的 Context（不可变，线程安全），
// `FromContext`/`HasLocale` 读 `Context.Locale`。Go 的 HasLocale 判"键是否存在"，所以 Context.Locale 用
// **可空**的 `Locale?` 表示：null = Go 的键不存在，非 null = 挂过（哪怕值是 Default）。
//
// 与 Go 的偏离（都写在报告里）：
//  1. `language.Tag` 是带 CLDR 匹配信息的结构体（脚本/地区/likely-subtags + 置信度）；C# 侧只保存
//     **规范化后的标签文本**。`TryParse` 按 BCP-47 子标签语法做校验并规范化大小写（语言小写、脚本
//     Titlecase、地区大写、其余小写），与 Go 的 language.Parse 对合法标签的规范化结果一致；但 Go 会接受
//     的孤例（grandfathered/私有用法 "i-klingon"、"x-foo"、3 字母语言 + 扩展子标签的顺序约束）这里只做
//     通用子标签校验，不做完整语法状态机。
//  2. Go 的 Parse 在语法错误时仍返回"尽力解析出的 tag"（调用点全都忽略它）；C# 的 TryParse 失败时把
//     locale 置为 Default（= Go 的零值 Locale），因为 Go 侧的 Parse 错误分支在所有调用点都被丢弃。
//  3. `language.MustParse`（Go 生成物里用来构造 matcher 的标签）→ C# 的 internal `FromTag`。
//  4. 匹配（locale → 本地化数据）不再走 language.NewMatcher，见 Diagnostics.cs 的 MatchLocale。
//
// **命名解析警告（跨 package 的硬约定）**：本 package 的目录名 = 命名空间 TypeScript.Locale，而类型也叫
// Locale。C# 的名字解析里**外层命名空间的成员优先于 using 导入的类型**：在 `namespace TypeScript.*;` 里
// 写 `using TypeScript.Locale;` 之后裸写 `Locale` 会 CS0118「Locale 是命名空间，但此处被当做类型使用」
// （实测；与 docs/architecture.md §2 里 internal/debug 用根命名空间规避的是同一个坑）。所以本文件之外
// 一律用别名：
//     using Loc = TypeScript.Locale.Locale;
// 类型名写成 `Loc`（别名只能叫 Loc —— 叫 Locale 同样会被命名空间吃掉），公开 API 里编译出来仍是
// TypeScript.Locale.Locale。本命名空间内部的代码不受影响（命名空间成员优先）。
// 例外：如果文件里的类型已经有叫 `Loc` 的成员（典型是 Go 的 `Diagnostic.Loc()`/`Node.Loc()` —— 方法会遮蔽
// 别名，与 docs/ast.md §2 里 SubtreeFacts 的坑同源），就在那个文件换一个别名（如 `using TSLoc = …;`）或
// 直接写全名 `TypeScript.Locale.Locale`。
using TypeScript.Core;

namespace TypeScript.Locale;

/// <summary>Go 的 <c>type Locale language.Tag</c>。</summary>
public readonly struct Locale : IEquatable<Locale>
{
    /// <summary>规范化后的 BCP-47 标签（如 <c>"de-DE"</c>）；<c>null</c> = Go 的零值（<c>language.Und</c>）。</summary>
    private readonly string? _tag;

    /// <summary>Go 的 <c>var Default Locale</c>（零值 = 未指定语言）。</summary>
    public static readonly Locale Default = default;

    private Locale(string? tag)
    {
        _tag = tag;
    }

    /// <summary>Go 的 <c>func (l Locale) String() string</c>：Default 返回空串，其余返回规范化后的标签。</summary>
    public string String()
    {
        return _tag ?? string.Empty;
    }

    /// <summary>Go 的 <c>Locale</c> 是 <c>fmt.Stringer</c>，C# 侧的对应物是 <see cref="ToString"/>（§3.3）。</summary>
    public override string ToString()
    {
        return String();
    }

    /// <summary>Go 的 <c>func Parse(localeStr string) (Locale, bool)</c>（§3.2：<c>(T, bool)</c> → <c>TryX</c>）。</summary>
    public static bool TryParse(string localeStr, out Locale locale)
    {
        locale = Default;
        if (string.IsNullOrEmpty(localeStr))
        {
            // Go: language.Parse("") 返回语法错误（Und）。
            return false;
        }

        // 规范化只改大小写，长度不变；长于 64 的标签走堆分配（避免给栈上留一个够大的缓冲）。
        Span<char> buffer = localeStr.Length <= 64 ? stackalloc char[64] : new char[localeStr.Length];
        var length = 0;
        var index = 0;
        var first = true;
        while (true)
        {
            var end = localeStr.IndexOf('-', index);
            if (end < 0)
            {
                end = localeStr.Length;
            }

            var subtag = localeStr.AsSpan(index, end - index);
            if (subtag.Length == 0 || subtag.Length > 8)
            {
                return false;
            }

            if (first)
            {
                // 主语言子标签：Go 允许 2..8 个字母（"en"、"fil"、"de"…）。
                if (subtag.Length < 2)
                {
                    return false;
                }

                for (var i = 0; i < subtag.Length; i++)
                {
                    if (!char.IsAsciiLetter(subtag[i]))
                    {
                        return false;
                    }
                }
            }
            else
            {
                for (var i = 0; i < subtag.Length; i++)
                {
                    if (!char.IsAsciiLetterOrDigit(subtag[i]))
                    {
                        return false;
                    }
                }
            }

            if (length != 0)
            {
                buffer[length++] = '-';
            }

            length += CanonicalizeSubtag(buffer[length..], subtag, first);
            first = false;

            if (end == localeStr.Length)
            {
                break;
            }

            index = end + 1;
        }

        var tag = new string(buffer[..length]);
        // Go 里 language.Und 就是 Tag 的零值，与 locale.Default 是同一个值；单一 "und" 标签（大小写不敏感
        // 的规范化结果是 "und"）同样落到零值上，于是 Default.String() == ""（与 Go 的 Locale.String() 一致）。
        locale = string.Equals(tag, "und", StringComparison.Ordinal) ? Default : new Locale(tag);
        return true;
    }

    /// <summary>Go 生成物里的 <c>language.MustParse</c>（见 internal/diagnostics/loc_generated.go）。</summary>
    internal static Locale FromTag(string tag)
    {
        if (!TryParse(tag, out var locale))
        {
            throw new DebugFailureException($"invalid locale tag: {tag}");
        }

        return locale;
    }

    /// <summary>主语言子标签（小写，如 <c>"de"</c>）；Default 是空串。供 Diagnostics 的 locale 匹配使用。</summary>
    internal string Language
    {
        get
        {
            var tag = _tag;
            if (string.IsNullOrEmpty(tag))
            {
                return string.Empty;
            }

            var dash = tag.IndexOf('-');
            return dash < 0 ? tag : tag[..dash];
        }
    }

    /// <summary>Go 的 <c>func WithLocale(ctx context.Context, locale Locale) context.Context</c>。</summary>
    public static Context WithLocale(Context ctx, Locale locale)
    {
        return new Context(ctx.RequestID, ctx.CheckerLifetime, ctx.CancellationToken, locale);
    }

    /// <summary>Go 的 <c>func FromContext(ctx context.Context) Locale</c>（没挂过时是 Go 的零值 Locale）。</summary>
    public static Locale FromContext(Context ctx)
    {
        return ctx.Locale ?? Default;
    }

    /// <summary>Go 的 <c>func HasLocale(ctx context.Context) bool</c>（判"键是否存在"，与值是不是 Default 无关）。</summary>
    public static bool HasLocale(Context ctx)
    {
        return ctx.Locale.HasValue;
    }

    /// <summary>Go 的 <c>Locale</c> 是可比较的结构体；两边都是 TryParse/FromTag 规范化过的标签，序数比较即等价。</summary>
    public bool Equals(Locale other)
    {
        return string.Equals(_tag, other._tag, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return obj is Locale other && Equals(other);
    }

    public override int GetHashCode()
    {
        return _tag is null ? 0 : StringComparer.Ordinal.GetHashCode(_tag);
    }

    public static bool operator ==(Locale left, Locale right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Locale left, Locale right)
    {
        return !left.Equals(right);
    }

    /// <summary>把子标签按 BCP-47 规范形式写进 <paramref name="destination"/>，返回写入长度。</summary>
    private static int CanonicalizeSubtag(Span<char> destination, ReadOnlySpan<char> subtag, bool first)
    {
        var allLetters = true;
        var allDigits = true;
        for (var i = 0; i < subtag.Length; i++)
        {
            allLetters &= char.IsAsciiLetter(subtag[i]);
            allDigits &= char.IsAsciiDigit(subtag[i]);
        }

        if (first)
        {
            // 语言子标签小写（Go: "DE-de" → "de-DE"）。
            subtag.ToLowerInvariant(destination);
            return subtag.Length;
        }

        if (allLetters && subtag.Length == 4)
        {
            // 脚本子标签（"hans"/"HANS" → "Hans"）。
            destination[0] = char.ToUpperInvariant(subtag[0]);
            subtag[1..].ToLowerInvariant(destination[1..]);
            return subtag.Length;
        }

        if ((allLetters && subtag.Length == 2) || (allDigits && subtag.Length == 3))
        {
            // 地区子标签（"de-de" → "de-DE"、"zh-419"）。
            subtag.ToUpperInvariant(destination);
            return subtag.Length;
        }

        // 变体/扩展/私有用法：小写（Go 的规范化同样把它们小写）。
        subtag.ToLowerInvariant(destination);
        return subtag.Length;
    }
}
