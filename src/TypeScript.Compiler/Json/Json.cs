// ported from: internal/json/json.go
//
// C# 侧的形状（docs/architecture.md §2）：Go 的包级函数放在与包同名的静态类 `Json` 里，
// Go 的 `io.Writer`/`io.Reader` → C# 的 `Stream`（字节语义），Go 的 `(T, error)` → 正常返回值 + 抛 JsonException。
//
// 与 Go 不一致的地方（同见 docs/json.md §2）：
//   1) Go 的 `Unmarshal(in []byte, out any, opts...)` 靠指针写回；C# 侧拆成三种入口：
//        · Unmarshal<T>      —— 不需要创建实例（T = object? / 标量 / string / bool / byte[] / 枚举）
//        · UnmarshalNew<T>   —— 用 `where T : new()` 创建实例（对象、List<T>、Dictionary<K,V>；Go 侧是 reflect.New）
//        · UnmarshalInto     —— 调用方给出实例（数组这种长度固定的目标只能走这里）
//      AOT 下不能用 Activator/MakeGenericType/Array.CreateInstance(Type,…)（IL3050），这是不得已的形状。
//   2) Go 的 Marshal 会强制注入 jsontext.AllowInvalidUTF8(true)；C# 侧字符串本身就是 UTF-16，
//      非 ASCII 一律原样写 UTF-8（转义修正见 OutputFilterStream）。没有对应的选项。
using System.Text;

namespace TypeScript.Json;

public static class Json
{
    // json.go 里的 var 块：BeginObject/EndObject/Null/BeginArray/EndArray。
    public static readonly JsonToken BeginObject = new(JsonKind.BeginObject, null, false);
    public static readonly JsonToken EndObject = new(JsonKind.EndObject, null, false);
    public static readonly JsonToken BeginArray = new(JsonKind.BeginArray, null, false);
    public static readonly JsonToken EndArray = new(JsonKind.EndArray, null, false);
    public static readonly JsonToken Null = new(JsonKind.Null, null, false);

    // Go 的 json.Marshal(in any, opts ...json.Options)
    public static byte[] Marshal(object? value, params JsonOptions[] options)
    {
        var buffer = new MemoryStream();
        MarshalWrite(buffer, value, options);
        return buffer.ToArray();
    }

    // Go 的 json.MarshalEncode(out *jsontext.Encoder, in any, opts ...json.Options)
    public static void MarshalEncode(Encoder output, object? value, params JsonOptions[] options)
    {
        ArgumentNullException.ThrowIfNull(output);
        // Go 会把 opts 合并到 encoder 自己的选项里；C# 侧 encoder 的形状（缩进）在构造时就定了，
        // 这里只让 opts 覆盖编码期选项（Deterministic）；没给 opts 就沿用 encoder 自己的。
        var settings = options.Length == 0 ? output.Settings : JsonSettings.From(options);
        JsonEncode.EncodeValue(output, value, settings);
    }

    // Go 的 json.MarshalWrite(out io.Writer, in any, opts ...json.Options)
    public static void MarshalWrite(Stream output, object? value, params JsonOptions[] options)
    {
        ArgumentNullException.ThrowIfNull(output);
        var encoder = new Encoder(output, options);
        try
        {
            JsonEncode.EncodeValue(encoder, value, encoder.Settings);
            encoder.Flush();
        }
        finally
        {
            encoder.Dispose();
        }
    }

    // Go 的 json.MarshalIndent(in any, prefix, indent string)
    public static byte[] MarshalIndent(object? value, string prefix, string indent)
    {
        if (prefix.Length == 0 && indent.Length == 0)
        {
            // WithIndentPrefix 和 WithIndent 都意味着多行输出，所以两个都空时跳过（照抄 json.go 的特判）。
            return Marshal(value);
        }

        return Marshal(value, WithIndentPrefix(prefix), WithIndent(indent));
    }

    // Go 的 json.MarshalIndentWrite(out io.Writer, in any, prefix, indent string)
    public static void MarshalIndentWrite(Stream output, object? value, string prefix, string indent)
    {
        if (prefix.Length == 0 && indent.Length == 0)
        {
            MarshalWrite(output, value);
            return;
        }

        MarshalWrite(output, value, WithIndentPrefix(prefix), WithIndent(indent));
    }

    // 解出一个不需要创建实例的 T：object?（形状解码）、标量、string、bool、char、byte[]、枚举。
    // 需要实例的类型请用 UnmarshalNew<T>() / UnmarshalInto(...)。
    public static T? Unmarshal<T>(ReadOnlySpan<byte> input, params JsonOptions[] options)
    {
        var decoder = new Decoder(input);
        return (T?)JsonDecode.DecodeValue(decoder, JsonSettings.From(options), typeof(T));
    }

    public static T? Unmarshal<T>(byte[] input, params JsonOptions[] options)
    {
        return Unmarshal<T>(input.AsSpan(), options);
    }

    // C# 侧新增（Go 侧是 reflect.New）：用 `new T()` 创建实例再解进去。
    // 适合：实现了 IJsonUnmarshaler 的对象、List<T>、Dictionary<K,V>、以及有无参构造的值类型/结构体。
    public static T UnmarshalNew<T>(ReadOnlySpan<byte> input, params JsonOptions[] options)
        where T : new()
    {
        var decoder = new Decoder(input);
        return JsonDecode.DecodeNew<T>(decoder, JsonSettings.From(options));
    }

    public static T UnmarshalNew<T>(byte[] input, params JsonOptions[] options)
        where T : new()
    {
        return UnmarshalNew<T>(input.AsSpan(), options);
    }

    // C# 侧新增：把 JSON 解进调用方给出的实例（Go 的 `json.Unmarshal(data, &v)`）。
    // 数组这类长度固定的目标只能走这里（C# 的 `new T()` 约束不接受数组类型）。
    public static void UnmarshalInto<T>(ReadOnlySpan<byte> input, ref T target, params JsonOptions[] options)
    {
        var decoder = new Decoder(input);
        var settings = JsonSettings.From(options);
        var boxed = (object?)target ?? throw new JsonException("json: target is null");
        JsonDecode.DecodeInto(decoder, boxed, settings);
        target = (T)boxed;
    }

    // Go 的 json.UnmarshalRead(in io.Reader, out any, opts ...json.Options)
    public static T? UnmarshalRead<T>(Stream input, params JsonOptions[] options)
    {
        return UnmarshalDecode<T>(new Decoder(input), options);
    }

    public static T UnmarshalReadNew<T>(Stream input, params JsonOptions[] options)
        where T : new()
    {
        return JsonDecode.DecodeNew<T>(new Decoder(input), JsonSettings.From(options));
    }

    // Go 的 json.UnmarshalDecode(in *jsontext.Decoder, out any, opts ...json.Options)
    public static T? UnmarshalDecode<T>(Decoder input, params JsonOptions[] options)
    {
        ArgumentNullException.ThrowIfNull(input);
        return (T?)JsonDecode.DecodeValue(input, JsonSettings.From(options), typeof(T));
    }

    public static T UnmarshalDecodeNew<T>(Decoder input, params JsonOptions[] options)
        where T : new()
    {
        ArgumentNullException.ThrowIfNull(input);
        return JsonDecode.DecodeNew<T>(input, JsonSettings.From(options));
    }

    public static void UnmarshalDecodeInto<T>(Decoder input, ref T target, params JsonOptions[] options)
    {
        ArgumentNullException.ThrowIfNull(input);
        var boxed = (object?)target ?? throw new JsonException("json: target is null");
        JsonDecode.DecodeInto(input, boxed, JsonSettings.From(options));
        target = (T)boxed;
    }

    // Go 的 jsontext.AllowDuplicateNames(allow bool)
    public static JsonOptions AllowDuplicateNames(bool allow)
    {
        return JsonOptions.Boolean(JsonOptions.OptionKind.AllowDuplicateNames, allow);
    }

    // Go 的 json.Deterministic(v bool)
    public static JsonOptions Deterministic(bool value)
    {
        return JsonOptions.Boolean(JsonOptions.OptionKind.Deterministic, value);
    }

    // Go 的 jsontext.WithIndent(indent string)
    public static JsonOptions WithIndent(string indent)
    {
        return JsonOptions.Text(JsonOptions.OptionKind.Indent, indent);
    }

    // Go 的 jsontext.WithIndentPrefix(prefix string)：internal/json 只通过 MarshalIndent 用它。
    internal static JsonOptions WithIndentPrefix(string prefix)
    {
        return JsonOptions.Text(JsonOptions.OptionKind.IndentPrefix, prefix);
    }

    // Go 的 json.NewDecoder(r io.Reader)
    public static Decoder NewDecoder(Stream input)
    {
        return new Decoder(input);
    }

    // C# 侧新增（Go 侧用 bytes.NewReader 再交给 NewDecoder）。
    public static Decoder NewDecoder(ReadOnlySpan<byte> input)
    {
        return new Decoder(input);
    }

    // Go 的 `string(b)`（jsnum 用 json.Marshal(float64) 的结果当 Number.String()）。
    internal static string MarshalToString(object? value, params JsonOptions[] options)
    {
        return Encoding.UTF8.GetString(Marshal(value, options));
    }
}
