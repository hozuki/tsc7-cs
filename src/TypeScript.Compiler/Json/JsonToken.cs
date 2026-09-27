// ported from: internal/json/json.go（`Value`/`Kind` 别名与 BeginObject/EndObject/Null/BeginArray/EndArray 常量）
//
// Go 的 jsontext.Token / jsontext.Kind 用 byte 表示种类（值就是对应 JSON 定界符的 ASCII 码），
// C# 侧同样把 JsonKind 的数值钉在 ASCII 上，这样调用点里 `kind == JsonKind.BeginObject` 与
// Go 的 `token.Kind() == jsontext.ObjectStart.Kind()` 一一对应。
namespace TypeScript.Json;

public enum JsonKind : byte
{
    BeginObject = (byte)'{',
    EndObject = (byte)'}',
    BeginArray = (byte)'[',
    EndArray = (byte)']',
    Null = (byte)'n',
    True = (byte)'t',
    False = (byte)'f',
    String = (byte)'"',
    Number = (byte)'0',
}

// ported from: internal/json/json.go（jsontext.Token 的对应物）
//
// Go 的 Token 带完整的负载（字符串/数字/布尔都能从 Token 里取出来）；C# 侧只需要：
//   * String：字符串值（ReadToken 时读取）
//   * Number：数字的原始文本（保持 Go 的字节形状，不做二次格式化）
//   * True/False：布尔值
public readonly struct JsonToken
{
    private readonly string? _stringValue;

    internal JsonToken(JsonKind kind, string? stringValue, bool boolValue)
    {
        Kind = kind;
        this._stringValue = stringValue;
        BoolValue = boolValue;
    }

    public JsonKind Kind { get; }

    // String → 字符串值；Number → 原始文本；其它 kind → ""。
    public string StringValue => _stringValue ?? "";

    // True → true，False → false，其它 kind → false。
    public bool BoolValue { get; }

    // C# 侧新增（Go 侧是 jsontext.String/Bool/Int/Float 这些构造 Token 的函数）：
    // token 级 API（Encoder.WriteToken / Decoder.ReadToken）需要能自己造 token。
    // String token 在对象里处于“等属性名”位置时会被写成属性名（与 Go 的 jsontext 一样）。
    public static JsonToken String(string value)
    {
        return new JsonToken(JsonKind.String, value, false);
    }

    // 原样写出的数字文本；用 FormatFloat 的形状才会与 Go 一致（见 FloatFormat）。
    public static JsonToken Number(string text)
    {
        return new JsonToken(JsonKind.Number, text, false);
    }

    public static JsonToken Bool(bool value)
    {
        return new JsonToken(value ? JsonKind.True : JsonKind.False, null, value);
    }

    public override string ToString()
    {
        return Kind switch
        {
            JsonKind.String => $"String({StringValue})",
            JsonKind.Number => $"Number({StringValue})",
            JsonKind.True => "True",
            JsonKind.False => "False",
            JsonKind.Null => "Null",
            JsonKind.BeginObject => "BeginObject",
            JsonKind.EndObject => "EndObject",
            JsonKind.BeginArray => "BeginArray",
            _ => "EndArray",
        };
    }
}
