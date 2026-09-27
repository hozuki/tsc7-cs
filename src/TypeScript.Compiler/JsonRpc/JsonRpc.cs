// ported from: internal/jsonrpc/jsonrpc.go
//
// C# 映射与偏离（逐条）：
// 1) Go 的 `JSONRPCVersion`/`ID` 实现的是 encoding/json v1 的 `MarshalJSON`/`UnmarshalJSON`；C# 侧按
//    docs/json.md §4 一律用 `IJsonMarshaler`/`IJsonUnmarshaler`（`MarshalJSONTo`/`UnmarshalJSONFrom`），
//    所以 `MarshalJSON`/`UnmarshalJSON` 两个方法名在 C# 侧不存在（api-parity 需要白名单）。
// 2) Go 的 `var ErrInvalidJSONRPCVersion = errors.New(...)` → C# 的 `InvalidJSONRPCVersionException`
//    （Go 的 errors.Is/error 返回在 C# 里是 throw/catch，见 docs/architecture.md §3.2）。仓库里已有先例：
//    Json.JsonException 就是"Go 的 error 值 → C# 异常"。
// 3) Go 的 `String() string` 保留方法名（同 locale.Locale 的先例），同时覆写 `ToString()`。
// 4) Go 的 `func (r *ResponseError) Error() string`（error 接口实现）不移植：api-parity 对 `Error`
//    这个名字有全局白名单（"Go 实现 error 接口的方法"）；C# 侧要文本就调 `String()`。
// 5) Go 的 `json.Value`（= `jsontext.Value`，本质 `[]byte` 的原始 JSON）→ C# 的 `byte[]`
//    （docs/architecture.md §3.2：`[]T` → `T[]`）。**注意**：本项目的 Json 包把 `byte[]` 当 base64 映射，
//    所以原始 JSON 的读写一律走 `Encoder.WriteValue`/`Decoder.ReadValue`，绝不把它当普通值传给 Json.Marshal。
using System.Globalization;
using TypeScript.Json;
using static TypeScript.Json.Json;
// `System.Text.Encoder/Decoder`（隐式 using）与 `TypeScript.Json.Encoder/Decoder` 同名 → CS0104，必须别名。
using JsonEncoder = TypeScript.Json.Encoder;
using JsonDecoder = TypeScript.Json.Decoder;

namespace TypeScript.JsonRpc;

/// <summary>
/// Go: <c>type JSONRPCVersion struct</c>。恒等于 <c>"2.0"</c>：写法固定，读到时校验。
/// </summary>
public readonly struct JSONRPCVersion : IJsonMarshaler, IJsonUnmarshaler
{
    private const string _versionText = "\"2.0\"";

    /// <summary>Go: <c>func (JSONRPCVersion) MarshalJSON() ([]byte, error)</c>。</summary>
    public void MarshalJSONTo(JsonEncoder encoder) => encoder.WriteValue(_versionText);

    /// <summary>Go: <c>func (*JSONRPCVersion) UnmarshalJSON(data []byte) error</c>（不等于 "2.0" 即报错）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder decoder)
    {
        var raw = decoder.ReadValue();
        if (!raw.AsSpan().SequenceEqual("\"2.0\""u8))
        {
            throw new InvalidJSONRPCVersionException();
        }
    }
}

/// <summary>Go 的 <c>var ErrInvalidJSONRPCVersion</c>。</summary>
public sealed class InvalidJSONRPCVersionException() : Exception("invalid JSON-RPC version");

/// <summary>
/// Go: <c>type ID struct</c>。JSON-RPC 的 id 可以是字符串或整数；<c>str != ""</c> 时算字符串 id
/// （与 Go 一致：空串 id 会被当成整数 0，Go 的零值结构体本来就是这样）。
/// </summary>
public sealed class ID : IJsonMarshaler, IJsonUnmarshaler, IEquatable<ID>
{
    private string _str = "";
    private int _int;

    public ID()
    {
    }

    private ID(string str, int value)
    {
        _str = str;
        _int = value;
    }

    /// <summary>Go: <c>func NewID(rawValue IntegerOrString) *ID</c>。</summary>
    public static ID NewID(IntegerOrString rawValue) =>
        rawValue.String is not null ? new ID(rawValue.String, 0) : new ID("", rawValue.Integer ?? 0);

    /// <summary>Go: <c>func NewIDString(str string) *ID</c>。</summary>
    public static ID NewIDString(string str) => new(str, 0);

    /// <summary>Go: <c>func NewIDInt(i int32) *ID</c>。</summary>
    public static ID NewIDInt(int i) => new("", i);

    /// <summary>Go: <c>func (id *ID) String() string</c>。</summary>
    public string String() => _str.Length != 0 ? _str : _int.ToString(CultureInfo.InvariantCulture);

    public override string ToString() => String();

    /// <summary>Go: <c>func (id *ID) MarshalJSON() ([]byte, error)</c>。</summary>
    public void MarshalJSONTo(JsonEncoder encoder)
    {
        if (_str.Length != 0)
        {
            encoder.WriteToken(JsonToken.String(_str));
            return;
        }

        encoder.WriteToken(JsonToken.Number(_int.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>Go: <c>func (id *ID) UnmarshalJSON(data []byte) error</c>。</summary>
    public void UnmarshalJSONFrom(JsonDecoder decoder)
    {
        var kind = decoder.PeekKind();
        if (kind == JsonKind.Null)
        {
            // Go 的 `*ID` 字段在 JSON null 时保持 nil 指针；C# 的对应物是 null 引用，由调用方处理
            // （Go 的 ID.UnmarshalJSON 只在被解引用时才跑）。
            decoder.ReadToken();
            throw new JsonException("json: cannot unmarshal null into ID");
        }

        if (kind == JsonKind.String)
        {
            _int = 0;
            _str = decoder.Decode<string>() ?? "";
            return;
        }

        _str = "";
        _int = decoder.Decode<int>();
    }

    /// <summary>Go: <c>func (id *ID) TryInt() (int32, bool)</c>（§3.2：(T,bool) → TryX）。</summary>
    public bool TryInt(out int value)
    {
        value = _int;
        return _str.Length == 0;
    }

    /// <summary>Go: <c>func (id *ID) MustInt() int32</c>（panic → Debug.Fail）。</summary>
    public int MustInt()
    {
        if (_str.Length != 0)
        {
            Debug.Fail("ID is not an integer");
        }

        return _int;
    }

    // Go 的 `jsonrpc.ID` 是**可比较的结构体**（按值相等），ipc.AsyncConn 就用它当 pending map 的键
    // （`map[jsonrpc.ID]chan *Message`）。C# 的类默认按引用比较，所以这里显式复刻值语义。
    public bool Equals(ID? other) => other is not null && _str == other._str && _int == other._int;

    public override bool Equals(object? obj) => obj is ID other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_str, _int);

    public static bool operator ==(ID? left, ID? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(ID? left, ID? right) => !(left == right);
}

/// <summary>Go: <c>type IntegerOrString struct</c>（NewID 的入参容器）。</summary>
public sealed class IntegerOrString
{
    public int? Integer;
    public string? String;
}

/// <summary>Go: <c>type ResponseError struct</c>（JSON-RPC 错误响应体）。</summary>
public sealed class ResponseError : IJsonMarshaler, IJsonUnmarshaler
{
    public int Code;
    public string Message = "";

    /// <summary>Go: <c>Data any</c>（<c>json:"data,omitzero"</c>）。</summary>
    public object? Data;

    // Go 的标准 JSON-RPC 错误码（jsonrpc.go 的 const 块）。
    public const int CodeParseError = -32700;
    public const int CodeInvalidRequest = -32600;
    public const int CodeMethodNotFound = -32601;
    public const int CodeInvalidParams = -32602;
    public const int CodeInternalError = -32603;

    /// <summary>Go: <c>func (r *ResponseError) String() string</c>。</summary>
    public string String()
    {
        try
        {
            Marshal(Data);
        }
        catch (JsonException)
        {
            // Go: `data, err := json.Marshal(r.Data); if err != nil { return fmt.Sprintf("[%d]: %s\n%v", …) }`
            // —— 失败时 data 是 nil []byte，`%v` 打印成 "[]"。
            return $"[{Code}]: {Message}\n[]";
        }

        return $"[{Code}]: {Message}";
    }

    public override string ToString() => String();

    public void MarshalJSONTo(JsonEncoder encoder)
    {
        encoder.WriteToken(BeginObject);
        encoder.WriteToken(JsonToken.String("code"));
        encoder.WriteToken(JsonToken.Number(Code.ToString(CultureInfo.InvariantCulture)));
        encoder.WriteToken(JsonToken.String("message"));
        encoder.WriteToken(JsonToken.String(Message));
        if (Data is not null)
        {
            encoder.WriteToken(JsonToken.String("data"));
            MarshalEncode(encoder, Data);
        }

        encoder.WriteToken(EndObject);
    }

    public void UnmarshalJSONFrom(JsonDecoder decoder)
    {
        if (decoder.ReadToken().Kind != JsonKind.BeginObject)
        {
            throw new JsonException("jsonrpc: expected an object for ResponseError");
        }

        while (decoder.PeekKind() != JsonKind.EndObject)
        {
            switch (decoder.ReadName())
            {
                case "code":
                    Code = decoder.Decode<int>();
                    break;
                case "message":
                    Message = decoder.Decode<string>() ?? "";
                    break;
                case "data":
                    Data = decoder.Decode<object?>();
                    break;
                default:
                    decoder.SkipValue();
                    break;
            }
        }

        decoder.ReadToken();
    }
}

/// <summary>Go: <c>type MessageKind int</c>（数值照 Go 的 iota）。</summary>
public enum MessageKind
{
    Notification = 0,
    Request = 1,
    Response = 2,
}

/// <summary>
/// Go: <c>type Message struct</c>。与 lsproto.Message 不同，这里 params/result 保持原始 JSON（C# 的 `byte[]`）。
/// </summary>
public sealed class Message : IJsonMarshaler, IJsonUnmarshaler
{
    public JSONRPCVersion JSONRPC;
    public ID? ID;
    public string Method = "";

    /// <summary>Go: <c>Params json.Value</c>（<c>json:"params,omitzero"</c>）。</summary>
    public byte[]? Params;

    /// <summary>Go: <c>Result json.Value</c>（<c>json:"result,omitzero"</c>）。</summary>
    public byte[]? Result;

    public ResponseError? Error;

    /// <summary>Go: <c>func (m *Message) Kind() MessageKind</c>。</summary>
    public MessageKind Kind()
    {
        if (ID is not null && Method.Length == 0)
        {
            return MessageKind.Response;
        }

        if (ID is null)
        {
            return MessageKind.Notification;
        }

        return MessageKind.Request;
    }

    /// <summary>Go: <c>func (m *Message) IsRequest() bool</c>。</summary>
    public bool IsRequest() => ID is not null && Method.Length != 0;

    /// <summary>Go: <c>func (m *Message) IsNotification() bool</c>。</summary>
    public bool IsNotification() => ID is null && Method.Length != 0;

    /// <summary>Go: <c>func (m *Message) IsResponse() bool</c>。</summary>
    public bool IsResponse() => ID is not null && Method.Length == 0;

    public void MarshalJSONTo(JsonEncoder encoder)
    {
        encoder.WriteToken(BeginObject);
        encoder.WriteToken(JsonToken.String("jsonrpc"));
        JSONRPC.MarshalJSONTo(encoder);
        if (ID is not null)
        {
            encoder.WriteToken(JsonToken.String("id"));
            ID.MarshalJSONTo(encoder);
        }

        if (Method.Length != 0)
        {
            encoder.WriteToken(JsonToken.String("method"));
            encoder.WriteToken(JsonToken.String(Method));
        }

        WriteRawMember(encoder, "params", Params);
        WriteRawMember(encoder, "result", Result);
        if (Error is not null)
        {
            encoder.WriteToken(JsonToken.String("error"));
            Error.MarshalJSONTo(encoder);
        }

        encoder.WriteToken(EndObject);
    }

    public void UnmarshalJSONFrom(JsonDecoder decoder)
    {
        if (decoder.ReadToken().Kind != JsonKind.BeginObject)
        {
            throw new JsonException("jsonrpc: expected an object for Message");
        }

        while (decoder.PeekKind() != JsonKind.EndObject)
        {
            switch (decoder.ReadName())
            {
                case "jsonrpc":
                    decoder.DecodeNew<JSONRPCVersion>();
                    break;
                case "id":
                    ID = ReadOptional<ID>(decoder);
                    break;
                case "method":
                    Method = decoder.Decode<string>() ?? "";
                    break;
                case "params":
                    Params = decoder.ReadValue();
                    break;
                case "result":
                    Result = decoder.ReadValue();
                    break;
                case "error":
                    Error = ReadOptional<ResponseError>(decoder);
                    break;
                default:
                    decoder.SkipValue();
                    break;
            }
        }

        decoder.ReadToken();
    }

    /// <summary>Go 的 <c>"params"/"result" json:"…,omitzero"</c>：nil 与空都省略。</summary>
    internal static void WriteRawMember(JsonEncoder encoder, string name, byte[]? value)
    {
        if (value is null || value.Length == 0)
        {
            return;
        }

        encoder.WriteToken(JsonToken.String(name));
        encoder.WriteValue(value);
    }

    /// <summary>读一个可空成员：null 字面量 → null，否则解成 T。</summary>
    internal static T? ReadOptional<T>(JsonDecoder decoder)
        where T : class, new()
    {
        if (decoder.PeekKind() == JsonKind.Null)
        {
            decoder.ReadToken();
            return null;
        }

        return decoder.DecodeNew<T>();
    }
}

/// <summary>Go: <c>type RequestMessage struct</c>（构造请求/通知消息用）。</summary>
public sealed class RequestMessage : IJsonMarshaler, IJsonUnmarshaler
{
    public JSONRPCVersion JSONRPC;
    public ID? ID;
    public string Method = "";
    public object? Params;

    public void MarshalJSONTo(JsonEncoder encoder)
    {
        encoder.WriteToken(BeginObject);
        encoder.WriteToken(JsonToken.String("jsonrpc"));
        JSONRPC.MarshalJSONTo(encoder);
        if (ID is not null)
        {
            encoder.WriteToken(JsonToken.String("id"));
            ID.MarshalJSONTo(encoder);
        }

        encoder.WriteToken(JsonToken.String("method"));
        encoder.WriteToken(JsonToken.String(Method));
        if (Params is not null)
        {
            encoder.WriteToken(JsonToken.String("params"));
            MarshalEncode(encoder, Params);
        }

        encoder.WriteToken(EndObject);
    }

    public void UnmarshalJSONFrom(JsonDecoder decoder)
    {
        if (decoder.ReadToken().Kind != JsonKind.BeginObject)
        {
            throw new JsonException("jsonrpc: expected an object for RequestMessage");
        }

        while (decoder.PeekKind() != JsonKind.EndObject)
        {
            switch (decoder.ReadName())
            {
                case "jsonrpc":
                    decoder.DecodeNew<JSONRPCVersion>();
                    break;
                case "id":
                    ID = Message.ReadOptional<ID>(decoder);
                    break;
                case "method":
                    Method = decoder.Decode<string>() ?? "";
                    break;
                case "params":
                    Params = decoder.Decode<object?>();
                    break;
                default:
                    decoder.SkipValue();
                    break;
            }
        }

        decoder.ReadToken();
    }
}

/// <summary>Go: <c>type ResponseMessage struct</c>（构造响应消息用）。</summary>
public sealed class ResponseMessage : IJsonMarshaler, IJsonUnmarshaler
{
    public JSONRPCVersion JSONRPC;
    public ID? ID;
    public object? Result;
    public ResponseError? Error;

    public void MarshalJSONTo(JsonEncoder encoder)
    {
        encoder.WriteToken(BeginObject);
        encoder.WriteToken(JsonToken.String("jsonrpc"));
        JSONRPC.MarshalJSONTo(encoder);
        if (ID is not null)
        {
            encoder.WriteToken(JsonToken.String("id"));
            ID.MarshalJSONTo(encoder);
        }

        if (Result is not null)
        {
            encoder.WriteToken(JsonToken.String("result"));
            MarshalEncode(encoder, Result);
        }

        if (Error is not null)
        {
            encoder.WriteToken(JsonToken.String("error"));
            Error.MarshalJSONTo(encoder);
        }

        encoder.WriteToken(EndObject);
    }

    public void UnmarshalJSONFrom(JsonDecoder decoder)
    {
        if (decoder.ReadToken().Kind != JsonKind.BeginObject)
        {
            throw new JsonException("jsonrpc: expected an object for ResponseMessage");
        }

        while (decoder.PeekKind() != JsonKind.EndObject)
        {
            switch (decoder.ReadName())
            {
                case "jsonrpc":
                    decoder.DecodeNew<JSONRPCVersion>();
                    break;
                case "id":
                    ID = Message.ReadOptional<ID>(decoder);
                    break;
                case "result":
                    Result = decoder.Decode<object?>();
                    break;
                case "error":
                    Error = Message.ReadOptional<ResponseError>(decoder);
                    break;
                default:
                    decoder.SkipValue();
                    break;
            }
        }

        decoder.ReadToken();
    }
}

/// <summary>
/// Go 的 <c>jsonrpc</c> 包级函数（jsonrpc.go 里没有包级函数——NewID/NewIDString/NewIDInt 是 ID 上的
/// 构造入口；本类的存在是为了让三个入口也能按 Go 的写法 `jsonrpc.NewIDString(...)` 调用）。
/// </summary>
public static class JsonRpc
{
    /// <summary>Go: <c>func NewID(rawValue IntegerOrString) *ID</c>。</summary>
    public static ID NewID(IntegerOrString rawValue) => ID.NewID(rawValue);

    /// <summary>Go: <c>func NewIDString(str string) *ID</c>。</summary>
    public static ID NewIDString(string str) => ID.NewIDString(str);

    /// <summary>Go: <c>func NewIDInt(i int32) *ID</c>。</summary>
    public static ID NewIDInt(int i) => ID.NewIDInt(i);
}
