// ported from: internal/json/json.go（`Decoder = jsontext.Decoder`、`NewDecoder`）
// + Go encoding/json/jsontext 的 Decoder 语义（ReadToken/PeekKind/ReadValue/SkipValue/StackDepth）。
//
// 与 Go 一致的模型：Decoder 是一条**扁平 token 流**，对象成员名也是 token（用 JsonKind.String 表示，
// 与 Go 的 jsontext 一样；TSGO 里 lsproto 的 unmarshalStruct 就依赖这一点：
// `for dec.PeekKind() != '}' { name := dec.ReadValue(); … json.UnmarshalDecode(dec, …) }`）。
// 因此 `Decode<T>`/`UnmarshalDecode` 读到什么 token 就解什么（名字 token 当字符串解），不会偷偷跳过名字；
// 名字请用 ReadName()（C# 侧新增的便利方法）或 ReadValue() 显式消费。
//
// 偏离 Go：Go 的 Decoder 从 io.Reader 增量拉字节；C# 侧在构造时把输入整体读进内存（一次缓冲），
// 之后用“按需重建 Utf8JsonReader + 保存 JsonReaderState”的方式增量解析（Utf8JsonReader 是 ref struct，
// 不能作为字段长期持有）。因为输入是完整的，isFinalBlock 一律为 true。
// 另一个偏离：一个 Decoder 只服务一个顶层值（Go 的 Decoder 允许连续多个顶层值）。
using System.Text;
using System.Text.Json;

namespace TypeScript.Json;

public sealed class Decoder
{
    private readonly byte[] _utf8;
    private int _position;
    private JsonReaderState _state;

    private JsonKind _currentKind;
    private string? _currentString;
    private string? _currentNumber;
    private string? _currentName;

    public Decoder(Stream input)
    {
        _utf8 = ReadAll(input);
    }

    public Decoder(byte[] input)
    {
        _utf8 = input;
    }

    public Decoder(ReadOnlySpan<byte> input)
    {
        _utf8 = input.ToArray();
    }

    // Go 的 jsontext.Decoder.StackDepth()：当前 token 的嵌套深度。
    public int StackDepth { get; private set; }

    // Go 的 jsontext.Decoder.InputOffset()：已消费的字节数。
    public long InputOffset => _position;

    // Go 的 jsontext.Decoder.PeekKind()：看下一个 token 的种类（不消费）。
    // 对象成员名按 Go 的约定返回 JsonKind.String。
    public JsonKind PeekKind()
    {
        var reader = CreateReader();
        if (!reader.Read())
        {
            throw new JsonException("json: unexpected end of input");
        }

        return ToKind(reader.TokenType);
    }

    // Go 的 jsontext.Decoder.ReadToken()：消费并返回下一个 token。名字 token 的字符串值见
    // JsonToken.StringValue（同时也会记到 LastName 里）。
    public JsonToken ReadToken()
    {
        var kind = Read();
        return kind switch
        {
            JsonKind.String => new JsonToken(kind, _currentString, false),
            JsonKind.Number => new JsonToken(kind, _currentNumber, false),
            JsonKind.True => new JsonToken(kind, null, true),
            _ => new JsonToken(kind, null, false),
        };
    }

    // C# 侧新增（Go 侧从 ReadToken 的 Token 里取名字）：消费一个成员名 token 并返回名字。
    public string ReadName()
    {
        var reader = CreateReader();
        _currentName = null;
        if (!reader.Read())
        {
            throw new JsonException("json: unexpected end of input");
        }

        if (reader.TokenType != JsonTokenType.PropertyName)
        {
            throw new JsonException($"json: expected property name, got {reader.TokenType}");
        }

        var name = reader.GetString() ?? "";
        StackDepth = reader.CurrentDepth + (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray ? 1 : 0);
        Update(ref reader);
        return name;
    }

    // Go 的 jsontext.Decoder.ReadValue()：原样读出下一个 token（Go 的 jsontext.Value 是 []byte）。
    // 与 Go 一样，成员名会连引号一起返回、且只消费名字本身（lsproto 的 unmarshalStruct 依赖这个形状：
    // `name := dec.ReadValue()` 之后紧跟 `json.UnmarshalDecode(dec, &v)` 读值）。
    public byte[] ReadValue()
    {
        var reader = CreateReader();
        if (!reader.Read())
        {
            throw new JsonException("json: unexpected end of input");
        }

        var start = (int)reader.TokenStartIndex;
        StackDepth = reader.CurrentDepth + (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray ? 1 : 0);
        int end;
        if (reader.TokenType is JsonTokenType.PropertyName or JsonTokenType.String)
        {
            // 名字/字符串：ValueSpan 是引号内的原始字节，所以长度再加两个引号
            //（STJ 读名字时连后面的冒号一起算进 BytesConsumed，不能直接用）。
            end = start + reader.ValueSpan.Length + 2;
        }
        else
        {
            if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
            {
                reader.Skip();
            }

            end = (int)reader.BytesConsumed;
            while (end > start && IsWhiteSpace(_utf8[_position + end - 1]))
            {
                end--;
            }
        }

        var value = _utf8.AsSpan(_position + start, end - start).ToArray();
        Update(ref reader);
        return value;
    }

    // Go 的 jsontext.Decoder.SkipValue()：跳过下一个 token（容器会连子节点一起跳过；
    // 成员名只跳过名字本身，与 ReadValue 一致）。
    public void SkipValue()
    {
        var reader = CreateReader();
        if (!reader.Read())
        {
            throw new JsonException("json: unexpected end of input");
        }

        StackDepth = reader.CurrentDepth + (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray ? 1 : 0);
        if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
        {
            reader.Skip();
        }

        Update(ref reader);
    }

    // Go 的 json.UnmarshalDecode 的 C# 形状：把下一个 token 解成一个 T（不需要创建实例的类型）。
    public T? Decode<T>(params JsonOptions[] options)
    {
        return (T?)JsonDecode.DecodeValue(this, JsonSettings.From(options), typeof(T));
    }

    // C# 侧新增（Go 侧是 reflect.New）：用 `new T()` 创建实例再解进去。
    public T DecodeNew<T>(params JsonOptions[] options)
        where T : new()
    {
        return JsonDecode.DecodeNew<T>(this, JsonSettings.From(options));
    }

    // Go 的 `json.UnmarshalDecode(dec, &v)`：把下一个 token 解进已有的实例。
    public void DecodeInto(object target, params JsonOptions[] options)
    {
        ArgumentNullException.ThrowIfNull(target);
        JsonDecode.DecodeInto(this, target, JsonSettings.From(options));
    }

    // ---- 映射层（Decode.cs）用的游标原语 ----

    // 消费下一个 token（名字 token 也照实返回 JsonKind.String），并返回它的种类。
    internal JsonKind Read()
    {
        var reader = CreateReader();
        _currentName = null;
        if (!reader.Read())
        {
            throw new JsonException("json: unexpected end of input");
        }

        if (reader.TokenType == JsonTokenType.PropertyName)
        {
            _currentName = reader.GetString() ?? "";
        }

        Capture(ref reader);
        Update(ref reader);
        return _currentKind;
    }

    // 刚读到的成员名（ReadName 之外，Read 读到名字 token 时也会设置）。
    internal string? LastName => _currentName;

    internal string CurrentString => _currentString ?? "";

    internal string CurrentNumber => _currentNumber ?? "";

    private void Capture(ref Utf8JsonReader reader)
    {
        _currentString = null;
        _currentNumber = null;
        switch (reader.TokenType)
        {
            case JsonTokenType.StartObject:
                _currentKind = JsonKind.BeginObject;
                break;
            case JsonTokenType.EndObject:
                _currentKind = JsonKind.EndObject;
                break;
            case JsonTokenType.StartArray:
                _currentKind = JsonKind.BeginArray;
                break;
            case JsonTokenType.EndArray:
                _currentKind = JsonKind.EndArray;
                break;
            case JsonTokenType.Null:
                _currentKind = JsonKind.Null;
                break;
            case JsonTokenType.True:
                _currentKind = JsonKind.True;
                break;
            case JsonTokenType.False:
                _currentKind = JsonKind.False;
                break;
            case JsonTokenType.PropertyName:
            case JsonTokenType.String:
                _currentKind = JsonKind.String;
                _currentString = reader.GetString() ?? "";
                break;
            case JsonTokenType.Number:
                _currentKind = JsonKind.Number;
                _currentNumber = Encoding.UTF8.GetString(reader.ValueSpan);
                break;
            default:
                throw new JsonException($"json: unsupported token type: {reader.TokenType}");
        }

        StackDepth = reader.CurrentDepth + (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray ? 1 : 0);
    }

    private Utf8JsonReader CreateReader()
    {
        return new Utf8JsonReader(_utf8.AsSpan(_position), isFinalBlock: true, _state);
    }

    private void Update(ref Utf8JsonReader reader)
    {
        _position += (int)reader.BytesConsumed;
        _state = reader.CurrentState;
    }

    private static JsonKind ToKind(JsonTokenType tokenType)
    {
        return tokenType switch
        {
            JsonTokenType.StartObject => JsonKind.BeginObject,
            JsonTokenType.EndObject => JsonKind.EndObject,
            JsonTokenType.StartArray => JsonKind.BeginArray,
            JsonTokenType.EndArray => JsonKind.EndArray,
            JsonTokenType.Null => JsonKind.Null,
            JsonTokenType.True => JsonKind.True,
            JsonTokenType.False => JsonKind.False,
            JsonTokenType.PropertyName => JsonKind.String,
            JsonTokenType.String => JsonKind.String,
            JsonTokenType.Number => JsonKind.Number,
            _ => throw new JsonException($"json: unsupported token type: {tokenType}"),
        };
    }

    private static bool IsWhiteSpace(byte b)
    {
        return b is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r';
    }

    private static byte[] ReadAll(Stream input)
    {
        if (input is MemoryStream memory && memory.Position == 0)
        {
            return memory.ToArray();
        }

        using var buffer = new MemoryStream();
        input.CopyTo(buffer);
        return buffer.ToArray();
    }
}
