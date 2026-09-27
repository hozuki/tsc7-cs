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

    private readonly OptionKind kind;
    private readonly bool boolValue;
    private readonly string? stringValue;

    private JsonOptions(OptionKind kind, bool boolValue, string? stringValue)
    {
        this.kind = kind;
        this.boolValue = boolValue;
        this.stringValue = stringValue;
    }

    internal OptionKind Kind => kind;

    internal bool BoolValue => boolValue;

    internal string StringValue => stringValue ?? "";

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
    internal bool Deterministic;
    internal bool AllowDuplicateNames;
    internal bool HasIndent;
    internal string Indent;
    internal string IndentPrefix;

    internal static JsonSettings From(JsonOptions[] options)
    {
        var settings = new JsonSettings
        {
            Deterministic = false,
            AllowDuplicateNames = false,
            HasIndent = false,
            Indent = "",
            IndentPrefix = "",
        };

        foreach (var option in options)
        {
            switch (option.Kind)
            {
                case JsonOptions.OptionKind.Deterministic:
                    settings.Deterministic = option.BoolValue;
                    break;
                case JsonOptions.OptionKind.AllowDuplicateNames:
                    settings.AllowDuplicateNames = option.BoolValue;
                    break;
                case JsonOptions.OptionKind.Indent:
                    settings.HasIndent = true;
                    settings.Indent = option.StringValue;
                    break;
                case JsonOptions.OptionKind.IndentPrefix:
                    settings.HasIndent = true;
                    settings.IndentPrefix = option.StringValue;
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
            if (!HasIndent)
            {
                return false;
            }

            if (IndentPrefix.Length != 0 || Indent.Length == 0)
            {
                return true;
            }

            var c = Indent[0];
            if (c != ' ' && c != '\t')
            {
                return true;
            }

            for (var i = 1; i < Indent.Length; i++)
            {
                if (Indent[i] != c)
                {
                    return true;
                }
            }

            return false;
        }
    }

    internal bool IndentIsTab => HasIndent && !NeedsReindent && Indent.Length > 0 && Indent[0] == '\t';
}
