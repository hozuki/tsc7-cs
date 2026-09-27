// C# 侧新增：Go 的 json.Options / jsontext.Options 在 C# 侧的载体。
//
// Go 侧是“不透明接口 + With* 函数返回实现该接口的私有类型”的选项组合；C# 用 readonly struct 承载
// 单个选项（种类 + 值），多个选项由 JsonSettings.From(options) 合并成一份可读的配置。
// 选项本身不分配（struct），所以调用点可以像 Go 的 `var allowInvalid []json.Options = …` 那样静态缓存。
//
// 注意：Go 的 `jsontext.WithIndent("")`（空缩进）**依然是多行输出**，所以这里用 HasIndent 记录
// “是否给过缩进选项”，而不是看缩进串是否为空（见 docs/json.md §1.2）。
namespace TypeScript.Json;

public readonly struct JsonOptions
{
    internal enum OptionKind : byte
    {
        Deterministic,
        AllowDuplicateNames,
        Indent,
        IndentPrefix,
    }

    private readonly OptionKind _kind;
    private readonly bool _boolValue;
    private readonly string? _stringValue;

    private JsonOptions(OptionKind kind, bool boolValue, string? stringValue)
    {
        this._kind = kind;
        this._boolValue = boolValue;
        this._stringValue = stringValue;
    }

    internal OptionKind Kind => _kind;

    internal bool BoolValue => _boolValue;

    internal string StringValue => _stringValue ?? "";

    internal static JsonOptions Boolean(OptionKind kind, bool value)
    {
        return new JsonOptions(kind, value, null);
    }

    internal static JsonOptions Text(OptionKind kind, string value)
    {
        return new JsonOptions(kind, false, value);
    }
}

// C# 侧新增：把 `params JsonOptions[]` 合并成一份不可变的配置快照，供 Encoder/Decoder/映射层读取。
internal struct JsonSettings
{
    internal bool _deterministic;
    internal bool _allowDuplicateNames;
    internal bool _hasIndent;
    internal string _indent;
    internal string _indentPrefix;

    internal static JsonSettings From(JsonOptions[] options)
    {
        var settings = new JsonSettings
        {
            _deterministic = false,
            _allowDuplicateNames = false,
            _hasIndent = false,
            _indent = "",
            _indentPrefix = "",
        };

        foreach (var option in options)
        {
            switch (option.Kind)
            {
                case JsonOptions.OptionKind.Deterministic:
                    settings._deterministic = option.BoolValue;
                    break;
                case JsonOptions.OptionKind.AllowDuplicateNames:
                    settings._allowDuplicateNames = option.BoolValue;
                    break;
                case JsonOptions.OptionKind.Indent:
                    settings._hasIndent = true;
                    settings._indent = option.StringValue;
                    break;
                case JsonOptions.OptionKind.IndentPrefix:
                    settings._hasIndent = true;
                    settings._indentPrefix = option.StringValue;
                    break;
            }
        }

        return settings;
    }

    // Utf8JsonWriter 只会“同一个字符重复 N 次”这一种缩进（IndentCharacter 只接受空格和制表符）。
    // 缩进串是这两种之一且没有 prefix 时可以直接用它；否则由 OutputFilterStream 逐行重排（见该文件）。
    internal bool NeedsReindent
    {
        get
        {
            if (!_hasIndent)
            {
                return false;
            }

            if (_indentPrefix.Length != 0 || _indent.Length == 0)
            {
                return true;
            }

            var c = _indent[0];
            if (c != ' ' && c != '\t')
            {
                return true;
            }

            for (var i = 1; i < _indent.Length; i++)
            {
                if (_indent[i] != c)
                {
                    return true;
                }
            }

            return false;
        }
    }

    internal bool IndentIsTab => _hasIndent && !NeedsReindent && _indent.Length > 0 && _indent[0] == '\t';
}
