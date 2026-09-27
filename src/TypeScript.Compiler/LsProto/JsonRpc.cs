// ported from: internal/lsp/lsproto/jsonrpc.go
//
// 本文件用 json/v2 的**宽松**结构体解码语义（指针字段遇 null 变 nil、没有 required 检查、未知成员忽略），
// 与 lsproto 的严格 unmarshalStruct（StructCodec.cs）不同：Go 这里调的是 `json.Unmarshal(data, &raw)`。
// C# 侧没有反射，所以三个消息类型的编解码都是手写的 token 循环，严格度逐条照抄 Go。
//
// 与 Go 的偏离（逐条）：
// 1) Go 的 `json.Value`（原始 JSON 字节）→ C# 的 `byte[]`，读是 `Decoder.ReadValue()`、写是
//    `Encoder.WriteValue()`；**不能**把它当普通值交给 `MarshalEncode`（Json 包把 `byte[]` 当 base64）。
//    `Params`/`Result` 是 `object?`：线上进来的消息装 `byte[]`，本进程内构造的消息装具体类型（Go 的 `any` 同理）。
// 2) Go 的未导出字段 `msg any` → C# 的 `_msg`（private；只有 AsRequest/AsResponse 与两个 Message() 工厂用）。
//    `Kind` 是导出字段，照 Go 保持 public 字段。
// 3) Go 的 `fmt.Errorf("%w: %w", ErrorCodeInvalidRequest, err)` → `ErrorCodeException(ErrorCode.InvalidRequest, …)`，
//    文本照 Go 拼成 `"InvalidRequest: <err>"`（ErrorCodeException 只用传入的 message 当 Message，不加前缀）。
//    （lsp.go 侧 `ErrorCode` 是 enum、`JsonException` 是 sealed，不能承载错误码；见 Lsp.cs）。
// 4) `omitzero` 在 `any` 字段上的判定见 `IsZeroValue`：json/v2 按**动态值**判零（接口本身非 nil 不算零），
//    lsproto 里唯一实现 `IsZero() bool` 的是 `NoParams`（NoParams 参数因此被整个省略；`Null` 没有 IsZero，
//    所以 `"result": null` 照写）。
// 5) `ResponseMessage` 在 Go 里没有自定义编解码（走默认结构体映射，因此 `id` 没有 omitzero：nil 也写 null）；
//    C# 侧没有反射，照样手写，且**不**加 ErrorCodeInvalidRequest 包装（Go 里就没有这一层）。
// 6) `ID`/`JSONRPCVersion`/`ResponseError`/`MessageKind` 复用 jsonrpc 包的同名类型（Go 也是复用 `jsonrpc`）。
using TypeScript.Json;
using TypeScript.JsonRpc;
using static TypeScript.Json.Json;
using static TypeScript.LsProto.LsProto;
// `System.Text.Encoder/Decoder`（隐式 using）与 TypeScript.Json.Encoder/Decoder 同名 → CS0104，必须别名。
using JsonEncoder = TypeScript.Json.Encoder;
using JsonDecoder = TypeScript.Json.Decoder;

namespace TypeScript.LsProto;

public static partial class LsProto
{
    /// <summary>
    /// Go: <c>func NewID(rawValue IntegerOrString) *jsonrpc.ID</c>（lsproto 有自己的 IntegerOrString，
    /// 所以这个包装存在）。
    /// </summary>
    public static ID NewID(IntegerOrString rawValue) =>
        rawValue.String is not null ? ID.NewIDString(rawValue.String) : ID.NewIDInt(rawValue.Integer!.Value);

    /// <summary>
    /// Go 的 json/v2 对 <c>any</c> 字段判 <c>omitzero</c> 的规则：nil 接口算零，或动态值实现
    /// <c>IsZero() bool</c> 且返回 true。lsproto 里只有 <see cref="NoParams"/> 实现了它
    /// （C# 没有反射，所以这里显式列出；新增带 IsZero 的类型时要同步这里）。
    /// </summary>
    internal static bool IsZeroValue(object? value) => value is null || value is NoParams;

    /// <summary>读一个可空成员（Go 的 <c>*T</c> 字段）：null 字面量 → null，否则解成 T。</summary>
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

    /// <summary>
    /// 写一个 <c>any</c> 值：原始 JSON（`byte[]` = Go 的 json.Value）原样写出，其余按动态类型编
    /// （Go 的 `json.Marshal` 对 json.Value 也是原样写）。
    /// </summary>
    internal static void WriteAny(JsonEncoder encoder, object value)
    {
        if (value is byte[] raw)
        {
            encoder.WriteValue(raw);
            return;
        }

        MarshalEncode(encoder, value);
    }
}

/// <summary>
/// Go: <c>type Message struct</c>（<c>Kind</c> + 未导出的 <c>msg any</c>）。与 jsonrpc.Message 不同，
/// 这里的 msg 是 lsproto 自己的 RequestMessage/ResponseMessage，params/result 在请求/响应内保持原始 JSON。
/// </summary>
public sealed class Message : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Kind jsonrpc.MessageKind</c>。</summary>
    public MessageKind Kind;

    /// <summary>Go 的未导出字段 <c>msg any</c>：<see cref="RequestMessage"/> 或 <see cref="ResponseMessage"/>。</summary>
    private object? _msg;

    public Message()
    {
    }

    internal Message(MessageKind kind, object msg)
    {
        Kind = kind;
        _msg = msg;
    }

    /// <summary>Go: <c>func (m *Message) AsRequest() *RequestMessage</c>（Go 的类型断言 panic ↔ 强制转换异常）。</summary>
    public RequestMessage AsRequest() => (RequestMessage)_msg!;

    /// <summary>Go: <c>func (m *Message) AsResponse() *ResponseMessage</c>。</summary>
    public ResponseMessage AsResponse() => (ResponseMessage)_msg!;

    /// <summary>
    /// Go: <c>func (m *Message) UnmarshalJSON(data []byte) error</c>。有 id 且没有 method → Response；
    /// 没有 id → Notification；否则 Request（三个分支都与 Go 逐字一致，包括两者都缺时的 Notification）。
    /// </summary>
    public void UnmarshalJSONFrom(JsonDecoder decoder)
    {
        ID? id = null;
        var method = default(Method);
        byte[]? paramsJson = null;
        byte[]? result = null;
        ResponseError? error = null;

        try
        {
            if (decoder.ReadToken().Kind != JsonKind.BeginObject)
            {
                throw new JsonException("lsproto: expected an object for Message");
            }

            while (decoder.PeekKind() != JsonKind.EndObject)
            {
                switch (decoder.ReadName())
                {
                    case "jsonrpc":
                        // Go 读进 raw.JSONRPC 并丢弃（只做 "2.0" 校验）。
                        decoder.DecodeNew<JSONRPCVersion>();
                        break;
                    case "method":
                        method = decoder.Decode<string>() ?? "";
                        break;
                    case "id":
                        id = ReadOptional<ID>(decoder);
                        break;
                    case "params":
                        // Go 的 `Params json.Value`：原样存字节，解码推迟到 UnmarshalParams。
                        paramsJson = decoder.ReadValue();
                        break;
                    case "result":
                        // 响应里没有 method，无从判断类型：同样存原始文本让调用方解。
                        result = decoder.ReadValue();
                        break;
                    case "error":
                        error = ReadOptional<ResponseError>(decoder);
                        break;
                    default:
                        decoder.SkipValue();
                        break;
                }
            }

            decoder.ReadToken();
        }
        catch (Exception ex) when (ex is JsonException or InvalidJSONRPCVersionException)
        {
            throw new ErrorCodeException(ErrorCode.InvalidRequest, $"{ErrorCode.InvalidRequest.String()}: {ex.Message}", ex);
        }

        if (id is not null && method.Value.Length == 0)
        {
            Kind = MessageKind.Response;
            _msg = new ResponseMessage { ID = id, Result = result, Error = error };
            return;
        }

        Kind = id is null ? MessageKind.Notification : MessageKind.Request;
        _msg = new RequestMessage { ID = id, Method = method, Params = paramsJson };
    }

    /// <summary>Go: <c>func (m *Message) MarshalJSON() ([]byte, error)</c>（写的就是 <c>_msg</c>，字段顺序同 Go）。</summary>
    public void MarshalJSONTo(JsonEncoder encoder) => MarshalEncode(encoder, _msg);
}

/// <summary>Go: <c>type RequestMessage struct</c>（请求或通知；通知就是没有 ID 的请求）。</summary>
public sealed class RequestMessage : IJsonMarshaler, IJsonUnmarshaler
{
    public JSONRPCVersion JSONRPC;
    public ID? ID;
    public Method Method;

    /// <summary>
    /// Go: <c>Params any</c>（<c>json:"params,omitzero"</c>）。线上消息里是原始 JSON（`byte[]`），
    /// 本进程构造的消息里是具体参数类型（如 <c>InitializeParams</c> 或 <see cref="NoParams"/>）。
    /// </summary>
    public object? Params;

    /// <summary>Go: <c>func (r *RequestMessage) Message() *Message</c>（没有 ID 就是通知）。</summary>
    public Message Message()
    {
        var kind = MessageKind.Request;
        if (ID is null)
        {
            kind = MessageKind.Notification;
        }

        return new Message(kind, this);
    }

    /// <summary>
    /// Go: 默认结构体映射（字段顺序 jsonrpc / id,omitzero / method / params,omitzero）。
    /// </summary>
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
        encoder.WriteToken(JsonToken.String(Method.Value));
        if (!IsZeroValue(Params))
        {
            encoder.WriteToken(JsonToken.String("params"));
            WriteAny(encoder, Params!);
        }

        encoder.WriteToken(EndObject);
    }

    /// <summary>
    /// Go: <c>func (r *RequestMessage) UnmarshalJSON(data []byte) error</c>（JSONRPC 只校验不回写；失败时
    /// 保持 r 不变）。
    /// </summary>
    public void UnmarshalJSONFrom(JsonDecoder decoder)
    {
        ID? id = null;
        var method = default(Method);
        byte[]? paramsJson = null;

        try
        {
            if (decoder.ReadToken().Kind != JsonKind.BeginObject)
            {
                throw new JsonException("lsproto: expected an object for RequestMessage");
            }

            while (decoder.PeekKind() != JsonKind.EndObject)
            {
                switch (decoder.ReadName())
                {
                    case "jsonrpc":
                        // Go 的 `raw.JSONRPC` 读了但没写回 r.JSONRPC（r 保持零值），这里同样丢弃。
                        decoder.DecodeNew<JSONRPCVersion>();
                        break;
                    case "id":
                        id = ReadOptional<ID>(decoder);
                        break;
                    case "method":
                        method = decoder.Decode<string>() ?? "";
                        break;
                    case "params":
                        paramsJson = decoder.ReadValue();
                        break;
                    default:
                        decoder.SkipValue();
                        break;
                }
            }

            decoder.ReadToken();
        }
        catch (Exception ex) when (ex is JsonException or InvalidJSONRPCVersionException)
        {
            throw new ErrorCodeException(ErrorCode.InvalidRequest, $"{ErrorCode.InvalidRequest.String()}: {ex.Message}", ex);
        }

        ID = id;
        Method = method;
        if (paramsJson is { Length: > 0 })
        {
            Params = paramsJson;
        }
    }
}

/// <summary>Go: <c>type ResponseMessage struct</c>。</summary>
public sealed class ResponseMessage : IJsonMarshaler, IJsonUnmarshaler
{
    public JSONRPCVersion JSONRPC;
    public ID? ID;

    /// <summary>Go: <c>Result any</c>（<c>json:"result,omitzero"</c>）：线上消息里是原始 JSON（`byte[]`）。</summary>
    public object? Result;

    public ResponseError? Error;

    /// <summary>Go: <c>func (r *ResponseMessage) Message() *Message</c>。</summary>
    public Message Message() => new(MessageKind.Response, this);

    /// <summary>
    /// Go: 默认结构体映射（字段顺序 jsonrpc / id / result,omitzero / error,omitzero）。注意 <c>id</c> 没有
    /// omitzero：nil 也写成 <c>null</c>。
    /// </summary>
    public void MarshalJSONTo(JsonEncoder encoder)
    {
        encoder.WriteToken(BeginObject);
        encoder.WriteToken(JsonToken.String("jsonrpc"));
        JSONRPC.MarshalJSONTo(encoder);
        encoder.WriteToken(JsonToken.String("id"));
        MarshalEncode(encoder, ID);
        if (!IsZeroValue(Result))
        {
            encoder.WriteToken(JsonToken.String("result"));
            WriteAny(encoder, Result!);
        }

        if (Error is not null)
        {
            encoder.WriteToken(JsonToken.String("error"));
            Error.MarshalJSONTo(encoder);
        }

        encoder.WriteToken(EndObject);
    }

    /// <summary>
    /// Go: 默认结构体映射（<c>result any</c> 按 JSON 形状解，不是 json.Value）。Go 侧没有自定义
    /// UnmarshalJSON，所以这里也不把错误包成 ErrorCodeInvalidRequest。
    /// </summary>
    public void UnmarshalJSONFrom(JsonDecoder decoder)
    {
        ID? id = null;
        object? result = null;
        ResponseError? error = null;

        if (decoder.ReadToken().Kind != JsonKind.BeginObject)
        {
            throw new JsonException("lsproto: expected an object for ResponseMessage");
        }

        while (decoder.PeekKind() != JsonKind.EndObject)
        {
            switch (decoder.ReadName())
            {
                case "jsonrpc":
                    decoder.DecodeNew<JSONRPCVersion>();
                    break;
                case "id":
                    id = ReadOptional<ID>(decoder);
                    break;
                case "result":
                    result = decoder.Decode<object?>();
                    break;
                case "error":
                    error = ReadOptional<ResponseError>(decoder);
                    break;
                default:
                    decoder.SkipValue();
                    break;
            }
        }

        decoder.ReadToken();

        ID = id;
        Result = result;
        Error = error;
    }
}
