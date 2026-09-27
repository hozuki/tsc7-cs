// ported from: internal/diagnostics/diagnostics.go
//
// 本文件是手写部分；同 package 的另外两个文件是生成物（tools/TypeScript.CodeGen/DiagnosticsGenerator.cs）：
//   Messages.Generated.cs       ← internal/diagnostics/diagnostics_generated.go（2213 条消息常量 + AllMessages）
//   Localizations.Generated.cs  ← internal/diagnostics/loc_generated.go（matcher 的标签表 + 每个 locale 的资源名）
// 本文件末尾的 Category.String 对应 internal/diagnostics/stringer_generated.go（`stringer -type=Category`）。
//
// 偏离与决定（逐条对应 Go 源）：
//  1. Go 的 `language.Tag` 是带 CLDR 匹配信息的结构体；C# 侧是规范化的标签字符串（见 Locale.cs 的头部说明），
//     所以 `language.Und` → `Locale.Default`（两者都是零值）。`getLocalizedMessages` 里的
//     `matcher.Match(loc)`（置信度 >= language.Low）落成 MatchLocale：精确标签匹配 → 主语言子标签匹配 →
//     English 兜底（+ zh 的 Hant/TW/HK/MO 特例）。diagnostics_test.go 的用例（English/Und/af-ZA 兜底、
//     de-DE/fr-FR/es-ES/ja-JP/zh-CN/ko-KR/ru-RU + tsctests 用的 "cs"）全部在这套规则下命中同样的数据。
//  2. 本地化数据：Go 用 `//go:embed loc/*.json.gz` + compress/gzip 解压 + encoding/json 反序列化成
//     `map[Key]string`（惰性 + sync.OnceValue 缓存）。C# 侧把 TSGO 已提交的同一批 loc/*.json.gz 作为
//     EmbeddedResource 嵌进 TypeScript.Compiler.csproj（LogicalName 固定为 TypeScript.Diagnostics.loc.<名字>.json.gz，
//     由生成器写进 Localizations.Generated.cs 的常量表 —— 不走反射枚举资源，AOT 安全），运行时用
//     Assembly.GetManifestResourceStream + GZipStream 解压、再用我们自己的 TypeScript.Json 解成
//     Dictionary<Key,string>（同样惰性 + 缓存）。数据字节与 TSGO 出货文件逐位相同（生成器只做逐字节复制）。
//  3. Go 的 `m.text`/`m.key` 等未导出字段 → C# 私有字段 + internal 的 `Text` 属性（同程序集可见）。
//  4. `func (m *Message) String() string`（Go 的 fmt.Stringer，只用于调试）→ 覆写 `ToString()`（§3.3）。
//  5. Go 的 `type Key string` → `readonly struct Key`：Go 里 `Key` 与 string 之间靠"无类型字符串常量"隐式
//     转换、`string(k)` 显式转换；C# 里对应 implicit（入） + explicit（出），`default(Key)` 规范成 ""（= Go 的零值）。
//  6. Format 里 Go 的 `strings.ToValidUTF8(arg, "\uFFFD")` 不移植：那是 Go 的 string 是 UTF-8 字节序列导致的
//     （内联的孤立代理项会被替换成 U+FFFD）。C# 的 string 与 JS 字符串同构、孤立代理项是合法值
//     （docs/architecture.md §3.1），照抄会把合法数据改坏。
//  7. `panic(...)` → `throw new DebugFailureException(...)`（与 Ast 生成物一致；panic 文本逐字保留）。
//  8. StringifyArgs 的 `fmt.Sprintf("%v", arg)` → 特判 string/bool/nil，其余走不变文化的 ToString；
//     整数语义一致，float 的 'g' 最短格式化在极端指数形式上与 Go 有差异（无调用点依赖，见报告）。
//  9. 热路径优化（不改语义）：占位符匹配用 `Group.ValueSpan` 解析下标（零分配），
//     占位符正则用 `RegexOptions.CultureInvariant` 的静态 Regex（与 Go 的 regexp.MustCompile 对应）。
using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using Loc = TypeScript.Locale.Locale;
using TypeScript.Json;
using static TypeScript.Json.Json;

namespace TypeScript.Diagnostics;

/// <summary>Go 的 <c>type Category int32</c>（数值与 Go 一致；stringer 见 <c>String</c> 扩展方法）。</summary>
public enum Category
{
    Warning = 0,
    Error = 1,
    Suggestion = 2,
    Message = 3,
}

/// <summary>Go 的 <c>type Key string</c>（消息在本地化数据里的键，形如 <c>"Identifier_expected_1003"</c>）。</summary>
public readonly struct Key : IEquatable<Key>
{
    private readonly string? _value;

    public Key(string value)
    {
        // Go 的零值 Key 是 ""；C# 的 default(Key) 会把字段置成 null，这里统一成空串，
        // 于是 default(Key) == new Key("")、GetHashCode/Equals 也自洽。
        _value = value ?? string.Empty;
    }

    public string Value => _value ?? string.Empty;

    // Go 里 Key 与 string 之间的转换：无类型字符串常量隐式（`Localize(loc, m, "", …)`），
    // `string(key)` 显式。C# 的对应物就是下面两个运算符。
    public static implicit operator Key(string value) => new(value);

    public static explicit operator string(Key key) => key.Value;

    public override string ToString() => Value;

    public bool Equals(Key other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is Key other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public static bool operator ==(Key left, Key right) => left.Equals(right);

    public static bool operator !=(Key left, Key right) => !left.Equals(right);
}

/// <summary>Go 的 <c>type Message struct</c>（Go 侧用 <c>*Message</c>；C# 侧是引用类型，语义相同）。</summary>
public sealed class Message
{
    private readonly int _code;
    private readonly Category _category;
    private readonly Key _key;
    private readonly string _text;
    private readonly bool _reportsUnnecessary;
    private readonly bool _elidedInCompatibilityPyramid;
    private readonly bool _reportsDeprecated;

    // Go 的结构体字面量 `&Message{code: …, category: …}` → 生成物调用的 internal 构造器
    // （默认 false 的三个开关对应 Go 里缺省的零值字段）。
    internal Message(
        int code,
        Category category,
        Key key,
        string text,
        bool reportsUnnecessary = false,
        bool elidedInCompatibilityPyramid = false,
        bool reportsDeprecated = false)
    {
        _code = code;
        _category = category;
        _key = key;
        _text = text;
        _reportsUnnecessary = reportsUnnecessary;
        _elidedInCompatibilityPyramid = elidedInCompatibilityPyramid;
        _reportsDeprecated = reportsDeprecated;
    }

    public int Code() => _code;

    public Category Category() => _category;

    public Key Key() => _key;

    public bool ReportsUnnecessary() => _reportsUnnecessary;

    public bool ElidedInCompatibilityPyramid() => _elidedInCompatibilityPyramid;

    public bool ReportsDeprecated() => _reportsDeprecated;

    /// <summary>Go 的 <c>func (m *Message) String() string</c>（只用于调试：英文原文）。</summary>
    public override string ToString() => _text;

    /// <summary>Go 的 <c>func (m *Message) Localize(locale locale.Locale, args ...any) string</c>。</summary>
    public string Localize(Loc locale, params object?[]? args)
    {
        return Diagnostics.Localize(locale, this, default, Diagnostics.StringifyArgs(args));
    }

    /// <summary>Go 同 package 里直接读的 <c>m.text</c>（Localize 的英文回退文本）。</summary>
    internal string Text => _text;
}

public static partial class Diagnostics
{
    /// <summary>Go 的 <c>func (category Category) Name() string</c>（TS 的 diagnosticCategoryName）。</summary>
    public static string Name(this Category category)
    {
        switch (category)
        {
            case Category.Warning:
                return "warning";
            case Category.Error:
                return "error";
            case Category.Suggestion:
                return "suggestion";
            case Category.Message:
                return "message";
            default:
                throw new DebugFailureException("Unhandled diagnostic category");
        }
    }

    /// <summary>internal/diagnostics/stringer_generated.go（<c>stringer -type=Category</c>）：<c>String()</c>。</summary>
    public static string String(this Category category)
    {
        switch (category)
        {
            case Category.Warning:
                return "CategoryWarning";
            case Category.Error:
                return "CategoryError";
            case Category.Suggestion:
                return "CategorySuggestion";
            case Category.Message:
                return "CategoryMessage";
            default:
                return $"Category({(int)category})";
        }
    }

    /// <summary>Go 的 <c>func Localize(locale locale.Locale, message *Message, key Key, args ...string) string</c>。
    /// <c>args</c> 可空（Go 的 nil 变参切片与空切片在本函数里语义相同，按 §3.2 用 <c>string[]?</c> 表达）。</summary>
    public static string Localize(Loc locale, Message? message, Key key, params string[]? args)
    {
        if (message is null)
        {
            message = KeyToMessage(key);
        }

        if (message is null)
        {
            throw new DebugFailureException("Unknown diagnostic message: " + key.Value);
        }

        var text = message.Text;
        if (GetLocalizedMessages(locale).TryGetValue(message.Key(), out var localized))
        {
            text = localized;
        }

        return Format(text, args);
    }

    /// <summary>Go 的 <c>func Format(text string, args []string) string</c>（args 为 nil = 空，与 Go 的 len() 判断一致）。</summary>
    public static string Format(string text, string[]? args)
    {
        if (args is null || args.Length == 0)
        {
            return text;
        }

        return _placeholderRegexp.Replace(text, match =>
        {
            if (!long.TryParse(match.Groups[1].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                || index >= args.Length)
            {
                throw new DebugFailureException("Invalid formatting placeholder");
            }

            return args[(int)index];
        });
    }

    /// <summary>Go 的 <c>func StringifyArgs(args ...any) []string</c>（args 为 nil = 空，同 Go 的 len() 判断）。</summary>
    public static string[] StringifyArgs(params object?[]? args)
    {
        if (args is null || args.Length == 0)
        {
            // Go 返回 nil slice；C# 侧按 docs/architecture.md §3.2 用空数组（两者只参与 len()/range）。
            return [];
        }

        var result = new string[args.Length];
        for (var i = 0; i < args.Length; i++)
        {
            result[i] = StringifyArg(args[i]);
        }

        return result;
    }

    /// <summary>Go 的 <c>func NewAdHocMessage(message string) *Message</c>。</summary>
    public static Message NewAdHocMessage(string message)
    {
        return new Message(-1, Category.Error, new Key("-1"), message);
    }

    /// <summary>Go 的 <c>func keyToMessage(key Key) *Message</c>（unexported → internal）。</summary>
    internal static Message? KeyToMessage(Key key)
    {
        return _messagesByKey.Value.TryGetValue(key, out var message) ? message : null;
    }

    /// <summary>Go 的 <c>func getLocalizedMessages(loc language.Tag) map[Key]string</c>（unexported → internal）。
    /// 没有本地化数据时返回空表（Go 侧是 nil map，唯一的消费方式是索引查找，语义相同）。</summary>
    internal static Dictionary<Key, string> GetLocalizedMessages(Loc loc)
    {
        if (loc == Loc.Default)
        {
            // Go: loc == language.Und → nil（直接用英文原文）。
            return _noLocalizedMessages;
        }

        return _localizedMessagesCache.GetOrAdd(loc, LoadLocalizedMessages);
    }

    // Go 的 `var messagesByKey = sync.OnceValue(func() map[Key]*Message {…})`。
    private static readonly Lazy<Dictionary<Key, Message>> _messagesByKey = new(BuildMessagesByKey);

    // Go 的 `var localizedMessagesCache sync.Map // map[language.Tag]map[Key]string`。
    private static readonly ConcurrentDictionary<Loc, Dictionary<Key, string>> _localizedMessagesCache = new();

    // Go 侧"nil map"的对应物。
    private static readonly Dictionary<Key, string> _noLocalizedMessages = [];

    // Go 的 `placeholderRegexp`（unexported，但同 package 的测试直接用 → C# 侧 internal，见 Properties/AssemblyInfo.cs）。
    internal static readonly Regex _placeholderRegexp = new(@"\{(\d+)\}", RegexOptions.CultureInvariant);

    private static Dictionary<Key, Message> BuildMessagesByKey()
    {
        var messages = new Dictionary<Key, Message>(AllMessages.Length);
        foreach (var message in AllMessages)
        {
            messages[message.Key()] = message;
        }

        return messages;
    }

    private static Dictionary<Key, string> LoadLocalizedMessages(Loc loc)
    {
        // 生成物：index → 解压后的 key→text 表；0（English）与越界都返回 null（Go 的 localeFuncs[0] == nil）。
        return LoadLocale(MatchLocale(loc)) ?? _noLocalizedMessages;
    }

    /// <summary>Go 的 <c>matcher.Match(loc)</c> + <c>localeFuncs[index]</c> 的下标：0 = English/未命中。</summary>
    private static int MatchLocale(Loc locale)
    {
        var tag = locale.String();
        if (tag.Length == 0)
        {
            // Go: language.Und（零值）。
            return 0;
        }

        // (a) 规范化标签的精确匹配（Go 的 matcher 给 High 置信度）。
        for (var i = 1; i < _supportedLocales.Length; i++)
        {
            if (_supportedLocales[i].String() == tag)
            {
                return i;
            }
        }

        var language = locale.Language;
        if (language.Length == 0)
        {
            return 0;
        }

        // (b) 中文的脚本/地区：CLDR 的默认是 zh-Hant/TW/HK/MO = 繁体 = zh-TW，其余 = 简体 = zh-CN。
        if (language == "zh")
        {
            var traditional = tag.Contains("-TW", StringComparison.Ordinal)
                || tag.Contains("-HK", StringComparison.Ordinal)
                || tag.Contains("-MO", StringComparison.Ordinal)
                || tag.Contains("-Hant", StringComparison.Ordinal);
            var wanted = traditional ? "zh-TW" : "zh-CN";
            for (var i = 1; i < _supportedLocales.Length; i++)
            {
                if (_supportedLocales[i].String() == wanted)
                {
                    return i;
                }
            }
        }

        // (c) 主语言子标签匹配（"cs" → cs-CZ、"de-AT" → de-DE、"pt-PT" → pt-BR；Go 的置信度 >= Low）。
        for (var i = 1; i < _supportedLocales.Length; i++)
        {
            if (_supportedLocales[i].Language == language)
            {
                return i;
            }
        }

        // 未命中 → English（Go 的 confidence < Low → nil → 英文原文）。
        return 0;
    }

    /// <summary>Go 的 <c>func loadLocaleData(data string) map[Key]string</c>：解压 + 反序列化嵌入的本地化数据。</summary>
    private static Dictionary<Key, string> LoadLocaleData(string resourceName)
    {
        using var stream = typeof(Diagnostics).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new DebugFailureException("failed to find embedded locale data: " + resourceName);
        try
        {
            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            var localized = UnmarshalReadNew<Dictionary<string, string>>(gzip);
            var result = new Dictionary<Key, string>(localized.Count);
            foreach (var (key, text) in localized)
            {
                result[new Key(key)] = text;
            }

            return result;
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException)
        {
            // Go 侧是 panic("failed to create gzip reader: " + err) / panic("failed to unmarshal locale data: " + err)。
            throw new DebugFailureException("failed to unmarshal locale data: " + ex.Message);
        }
    }

    /// <summary>Go 的 <c>fmt.Sprintf("%v", arg)</c>：string 原样、bool 小写、nil → "&lt;nil&gt;"、其余不变文化。</summary>
    private static string StringifyArg(object? arg)
    {
        switch (arg)
        {
            case null:
                return "<nil>";
            case string s:
                return s;
            case bool b:
                return b ? "true" : "false";
            default:
                return Convert.ToString(arg, CultureInfo.InvariantCulture) ?? string.Empty;
        }
    }
}
