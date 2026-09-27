// ported from: internal/lsp/lsproto/lsp.go
//
// 与 Go 的偏离（逐条）：
// 1) Go 的 `panic(...)` → `Debug.Fail(...)`（抛 DebugFailureException）；Debug.Fail 没有 `[DoesNotReturn]`，
//    所以其后补 `throw new System.Diagnostics.UnreachableException()`（`using System.Diagnostics;` 全项目禁用）。
// 2) Go 的 `(T, error)` → 正常路径返回值、错误路径抛异常（docs/architecture.md §3.2）。协议错误的落点分两类：
//    * errNotObject/errNull/errMissing/errInvalidKind/errInvalidValue/errLiteralMismatch → `JsonException`。
//      理由：JsonException 是 sealed（无法继承出 LSP 专用子类），而 StructCodec 的 TryUnmarshalInto 只 catch 它
//      ——联合体的“逐个试”必须吞掉这些错误（Go 里就是 json.Unmarshal 返回的 error）。
//    * 带 Go 错误值的错误（ErrorCodeInvalidParams 等，Go 里 ErrorCode 自己实现 error 接口）→ `ErrorCodeException`，
//      对应调用方的 `errors.AsType[lsproto.ErrorCode]`（server.go 用它决定响应的错误码）。
// 3) 包级函数进同名静态类 `LsProto`（docs/architecture.md §2），同名常量落在类型上（§3.3）；
//    命名 string 类型的包装（Value/implicit/explicit/相等性）与生成物 Lsp.Generated.Enums.cs 里的写法一致。
// 4) Go 的 context.Context value → `AsyncLocal`（LSP 服务是单流程；值随 async 流传播，语义与派生 ctx 相同）：
//    WithClientCapabilities/GetClientCapabilities 因此不再接收 ctx 参数。
// 5) `RequestInfo` 多一个解码委托：Go 的 UnmarshalResult 靠 reflect 把 result 解进 Resp，C# 不能反射，
//    分派由生成器在每个 info 变量处写死（见 Lsp.Generated.Methods.cs 与 artifacts/lsproto-gen/generate.mjs）。
// 6) `RequestMessage.UnmarshalParams[T]` 是扩展方法（RequestMessage 在 JsonRpc.cs，跨文件加不了实例方法）；
//    调用点写法与 Go 逐字一致：`req.UnmarshalParams<InitializeParams>()`。

using System.Text;
using TypeScript.Json;
using static TypeScript.Json.Json;
using JsonEncoder = TypeScript.Json.Encoder;
using JsonDecoder = TypeScript.Json.Decoder;
using JsonRpcID = TypeScript.JsonRpc.ID;
using Path = TypeScript.TSPath.Path;
using static TypeScript.TSPath.TSPath;

namespace TypeScript.LsProto;

/// <summary>Go: <c>type DocumentUri string</c>（lsp.go:16）。</summary>
public readonly partial struct DocumentUri : IEquatable<DocumentUri>, IJsonMarshaler
{
    private readonly string? _value;

    /// <summary>Go 的 <c>DocumentUri(value)</c> 转换（入向 implicit、出向 explicit）。</summary>
    public DocumentUri(string value) => _value = value;

    /// <summary>Go 的 <c>string(uri)</c>。</summary>
    public string Value => _value ?? "";

    public static implicit operator DocumentUri(string value) => new(value);

    public static explicit operator string(DocumentUri value) => value.Value;

    public override string ToString() => Value;

    public bool Equals(DocumentUri other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is DocumentUri other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public static bool operator ==(DocumentUri left, DocumentUri right) => left.Equals(right);

    public static bool operator !=(DocumentUri left, DocumentUri right) => !left.Equals(right);

    /// <summary>Go: <c>func (m DocumentUri) MarshalJSONTo(enc *json.Encoder) error</c>：命名 string 类型按字符串写出。</summary>
    public void MarshalJSONTo(JsonEncoder encoder) => encoder.WriteToken(JsonToken.String(Value));

    /// <summary>Go: <c>func (uri DocumentUri) FileName() string</c>（lsp.go:20）。</summary>
    public string FileName()
    {
        var uri = Value;
        if (Bundled.Bundled.IsBundled(uri))
        {
            return uri;
        }

        if (uri.StartsWith("file://", StringComparison.Ordinal))
        {
            Uri parsed;
            try
            {
                // Go 的 url.Parse：Host 为空时用 Path（已解码），非空时用 //Host+Path。
                parsed = new Uri(uri, UriKind.Absolute);
            }
            catch (UriFormatException)
            {
                Debug.Fail($"invalid file URI: {uri}");
                throw new System.Diagnostics.UnreachableException();
            }

            // Go 的 url.URL.Path 是**已解码**的路径；C# 的 AbsolutePath 仍是转义形态（%20 仍是 %20），这里解码回原样。
            var filePath = Uri.UnescapeDataString(parsed.AbsolutePath);
            if (parsed.Host.Length != 0)
            {
                return "//" + parsed.Host + filePath;
            }

            return FixWindowsUriPath(filePath);
        }

        // Leave all other URIs escaped so we can round-trip them.
        var colon = uri.IndexOf(':');
        if (colon < 0)
        {
            Debug.Fail($"invalid URI: {uri}");
            throw new System.Diagnostics.UnreachableException();
        }

        var scheme = uri[..colon];
        var path = uri[(colon + 1)..];
        var authority = "ts-nul-authority";
        if (path.StartsWith("//", StringComparison.Ordinal))
        {
            var rest = path[2..];
            var slash = rest.IndexOf('/');
            if (slash < 0)
            {
                Debug.Fail($"invalid URI: {uri}");
                throw new System.Diagnostics.UnreachableException();
            }

            authority = rest[..slash];
            path = rest[(slash + 1)..];
        }

        return "^/" + scheme + "/" + authority + "/" + path;
    }

    /// <summary>Go: <c>func (uri DocumentUri) Path(useCaseSensitiveFileNames bool) tspath.Path</c>（lsp.go:35）。</summary>
    public Path Path(bool useCaseSensitiveFileNames) => ToPath(FileName(), "", useCaseSensitiveFileNames);

    /// <summary>Go: <c>func fixWindowsURIPath(path string) string</c>（lsp.go:40）。</summary>
    private static string FixWindowsUriPath(string path)
    {
        if (path.StartsWith('/') && path.Length >= 3 && IsVolumeCharacter(path[1]) && path[2] == ':')
        {
            return path[1..];
        }

        return path;
    }
}

/// <summary>Go: <c>type HasTextDocumentURI interface</c>（lsp.go:49）。</summary>
public interface HasTextDocumentURI
{
    DocumentUri TextDocumentURI();
}

/// <summary>Go: <c>type HasTextDocumentPosition interface</c>（lsp.go:53）。</summary>
public interface HasTextDocumentPosition : HasTextDocumentURI
{
    Position TextDocumentPosition();
}

/// <summary>Go: <c>type HasLocations interface</c>（lsp.go:58）。</summary>
public interface HasLocations
{
    Location[]? GetLocations();
}

/// <summary>Go: <c>type HasLocation interface</c>（lsp.go:62）。</summary>
public interface HasLocation
{
    Location GetLocation();
}

/// <summary>Go: <c>type URI string</c>（lsp.go:66）。</summary>
public readonly partial struct URI : IEquatable<URI>, IJsonMarshaler
{
    private readonly string? _value;

    /// <summary>Go 的 <c>URI(value)</c> 转换（入向 implicit、出向 explicit）。</summary>
    public URI(string value) => _value = value;

    /// <summary>Go 的 <c>string(uri)</c>。</summary>
    public string Value => _value ?? "";

    public static implicit operator URI(string value) => new(value);

    public static explicit operator string(URI value) => value.Value;

    public override string ToString() => Value;

    public bool Equals(URI other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is URI other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public static bool operator ==(URI left, URI right) => left.Equals(right);

    public static bool operator !=(URI left, URI right) => !left.Equals(right);

    /// <summary>Go: <c>func (m URI) MarshalJSONTo(enc *json.Encoder) error</c>。</summary>
    public void MarshalJSONTo(JsonEncoder encoder) => encoder.WriteToken(JsonToken.String(Value));
}

/// <summary>Go: <c>type Method string</c>（lsp.go:68）。常量在 Lsp.Generated.Methods.cs 的 partial 声明里。</summary>
public readonly partial struct Method : IEquatable<Method>, IJsonMarshaler
{
    private readonly string? _value;

    /// <summary>Go 的 <c>Method(value)</c> 转换（入向 implicit、出向 explicit）。</summary>
    public Method(string value) => _value = value;

    /// <summary>Go 的 <c>string(m)</c>。</summary>
    public string Value => _value ?? "";

    public static implicit operator Method(string value) => new(value);

    public static explicit operator string(Method value) => value.Value;

    public override string ToString() => Value;

    public bool Equals(Method other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is Method other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public static bool operator ==(Method left, Method right) => left.Equals(right);

    public static bool operator !=(Method left, Method right) => !left.Equals(right);

    /// <summary>Go: <c>func (m Method) MarshalJSONTo(enc *json.Encoder) error</c>。</summary>
    public void MarshalJSONTo(JsonEncoder encoder) => encoder.WriteToken(JsonToken.String(Value));
}

/// <summary>Go: <c>type Null struct{}</c>（lsp.go:263）：响应体为 null 的方法用它。</summary>
public sealed class Null : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>func (Null) UnmarshalJSONFrom(dec *json.Decoder) error</c>。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        var data = dec.ReadValue();
        if (!data.AsSpan().SequenceEqual("null"u8))
        {
            throw new JsonException($"expected null, got {Encoding.UTF8.GetString(data)}");
        }
    }

    /// <summary>Go: <c>func (Null) MarshalJSONTo(enc *json.Encoder) error</c>（<c>enc.WriteToken(json.Null)</c>）。</summary>
    public void MarshalJSONTo(JsonEncoder enc) => enc.WriteToken(TypeScript.Json.Json.Null);
}

/// <summary>Go: <c>type NoParams struct{}</c>（lsp.go:277）：声明为“无参数”的方法用它（见 UnmarshalParams）。</summary>
public sealed class NoParams
{
    /// <summary>Go: <c>func (NoParams) IsZero() bool</c>（恒 true，json 的 omitzero 用它）。</summary>
    public bool IsZero() => true;
}

/// <summary>Go: <c>type RequestInfo[Params, Resp any] struct</c>（lsp.go:159）。</summary>
public readonly struct RequestInfo<Params, Resp>
{
    /// <summary>Go 的 <c>Method Method</c> 字段。</summary>
    public readonly Method Method;

    /// <summary>C# 侧新增：Go 的 UnmarshalResult 靠 reflect 分配 Resp，C# 不能反射，解码由生成器写死。</summary>
    private readonly Func<JsonDecoder, Resp> _decodeResult;

    /// <summary>Go 的 <c>RequestInfo[Params, Resp]{Method: …}</c> 复合字面量。</summary>
    public RequestInfo(Method method, Func<JsonDecoder, Resp> decodeResult)
    {
        Method = method;
        _decodeResult = decodeResult;
    }

    /// <summary>Go: <c>func (info RequestInfo[Params, Resp]) UnmarshalResult(result any) (Resp, error)</c>（lsp.go:164）。</summary>
    public Resp UnmarshalResult(object? result)
    {
        if (result is not byte[] raw)
        {
            throw new JsonException($"expected json.Value, got {(result is null ? "<nil>" : result.GetType().Name)}");
        }

        return _decodeResult(new JsonDecoder(raw));
    }

    /// <summary>Go: <c>func (info RequestInfo[Params, Resp]) NewRequestMessage(id *jsonrpc.ID, params Params) *RequestMessage</c>（lsp.go:175）。</summary>
    public RequestMessage NewRequestMessage(JsonRpcID? id, Params parameters) => new() { ID = id, Method = Method, Params = parameters };
}

/// <summary>Go: <c>type NotificationInfo[Params any] struct</c>（lsp.go:184）。</summary>
public readonly struct NotificationInfo<Params>
{
    /// <summary>Go 的 <c>Method Method</c> 字段。</summary>
    public readonly Method Method;

    /// <summary>Go 的 <c>NotificationInfo[Params]{Method: …}</c> 复合字面量。</summary>
    public NotificationInfo(Method method) => Method = method;

    /// <summary>Go: <c>func (info NotificationInfo[Params]) NewNotificationMessage(params Params) *RequestMessage</c>（lsp.go:189）。</summary>
    public RequestMessage NewNotificationMessage(Params parameters) => new() { Method = Method, Params = parameters };
}

/// <summary>Go: <c>type ErrorCode</c> 自己实现 error 接口（<c>Error()</c> = <c>String()</c>），
/// 调用方用 <c>errors.AsType[lsproto.ErrorCode]</c> 取出响应错误码（lsp/server.go:1116）。
/// C# 的错误路径抛异常，所以用这个类型承载错误码——<c>JsonException</c> 是 sealed，不能作为基类。</summary>
public sealed class ErrorCodeException(ErrorCode code, string message, Exception? inner = null) : Exception(message, inner)
{
    /// <summary>Go 的 <c>lsproto.ErrorCode</c> 错误值。</summary>
    public readonly ErrorCode Code = code;
}

public static partial class LsProto
{
    // ---- Go 的 lsp.go 错误构造函数 ------------------------------------------------------------

    /// <summary>Go: <c>func errNotObject(k json.Kind) error</c>（lsp.go:70）。</summary>
    internal static JsonException ErrNotObject(JsonKind k) => new($"expected object start, but encountered {KindString(k)}");

    /// <summary>Go: <c>func errNull(field string) error</c>（lsp.go:74）。</summary>
    internal static JsonException ErrNull(string field) => new($"null value is not allowed for field \"{field}\"");

    /// <summary>Go: <c>func errMissing(props []string) error</c>（lsp.go:78）。</summary>
    internal static JsonException ErrMissing(List<string> props) => new($"missing required properties: {string.Join(", ", props)}");

    /// <summary>Go: <c>func errInvalidKind(typeName string, got json.Kind) error</c>（lsp.go:82）。</summary>
    internal static JsonException ErrInvalidKind(string typeName, JsonKind got) => new($"invalid {typeName}: got {KindString(got)}");

    /// <summary>Go: <c>func errInvalidValue(typeName string, data []byte) error</c>（lsp.go:86）。</summary>
    internal static JsonException ErrInvalidValue(string typeName, byte[] data) => new($"invalid {typeName}: {Encoding.UTF8.GetString(data)}");

    /// <summary>Go: <c>func errLiteralMismatch(typeName string, expected string, got []byte) error</c>（lsp.go:90）。</summary>
    internal static JsonException ErrLiteralMismatch(string typeName, string expected, byte[] got) =>
        new($"expected {typeName} value {expected}, got {Encoding.UTF8.GetString(got)}");

    /// <summary>Go 的 <c>jsontext.Kind.String()</c>（<c>%v</c> 的形状：null/false/true/string/number/{/}/[/]）。</summary>
    private static string KindString(JsonKind kind)
    {
        return kind switch
        {
            JsonKind.Null => "null",
            JsonKind.False => "false",
            JsonKind.True => "true",
            JsonKind.String => "string",
            JsonKind.Number => "number",
            JsonKind.BeginObject => "{",
            JsonKind.EndObject => "}",
            JsonKind.BeginArray => "[",
            JsonKind.EndArray => "]",
            _ => "",
        };
    }

    // ---- Go 的 lsp.go 断言（panic → Debug.Fail）-----------------------------------------------

    /// <summary>Go: <c>func assertOnlyOne(message string, count int)</c>（lsp.go:94）。</summary>
    internal static void AssertOnlyOne(string message, int count)
    {
        if (count != 1)
        {
            Debug.Fail(message);
        }
    }

    /// <summary>Go: <c>func assertAtMostOne(message string, count int)</c>（lsp.go:100）。</summary>
    internal static void AssertAtMostOne(string message, int count)
    {
        if (count > 1)
        {
            Debug.Fail(message);
        }
    }

    // ---- Go 的 lsp.go 原始 JSON 扫描（联合体的判别式探测）--------------------------------------

    /// <summary>Go: <c>func jsonKeyCheck(name []byte, key string) bool</c>（lsp.go:106）：
    /// 把带引号的原始 key token 与 key 的 UTF-8 字节比较。</summary>
    internal static bool JsonKeyCheck(byte[] name, string key)
    {
        if (name.Length < 2 || name[0] != (byte)'"' || name[^1] != (byte)'"')
        {
            return false;
        }

        var inner = name.AsSpan(1, name.Length - 2);
        var byteCount = Encoding.UTF8.GetByteCount(key);
        if (inner.Length != byteCount)
        {
            return false;
        }

        Span<byte> buffer = byteCount <= 128 ? stackalloc byte[128] : new byte[byteCount];
        Encoding.UTF8.GetBytes(key.AsSpan(), buffer);
        return buffer[..byteCount].SequenceEqual(inner);
    }

    /// <summary>Go: <c>func jsonObjectRawField(data []byte, field string) json.Value</c>（lsp.go:113）：
    /// 扫顶层 key 找 field，返回它的原始 JSON 值（含引号）；没找到或出错返回 null（= Go 的 nil）。</summary>
    internal static byte[]? JsonObjectRawField(byte[] data, string field)
    {
        if (data.Length == 0)
        {
            return null;
        }

        try
        {
            var dec = new JsonDecoder(data);
            if (dec.PeekKind() != JsonKind.BeginObject)
            {
                return null;
            }

            dec.ReadToken();
            while (dec.PeekKind() != JsonKind.EndObject)
            {
                var name = dec.ReadValue();
                if (JsonKeyCheck(name, field))
                {
                    return dec.ReadValue();
                }

                dec.SkipValue();
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Go: <c>func jsonObjectHasKey(data []byte, keys ...string) int</c>（lsp.go:139）：
    /// 返回第一个命中的 key 下标，没有则 -1；命中即返回（不解任何值）。</summary>
    internal static int JsonObjectHasKey(byte[] data, string key0)
    {
        return JsonObjectHasKeyCore(data, key0, null, null);
    }

    internal static int JsonObjectHasKey(byte[] data, string key0, string key1)
    {
        return JsonObjectHasKeyCore(data, key0, key1, null);
    }

    internal static int JsonObjectHasKey(byte[] data, string key0, string key1, string key2)
    {
        return JsonObjectHasKeyCore(data, key0, key1, key2);
    }

    // Go 的 keys ...string 在 C# 侧拆成 1/2/3 个 key 的重载（避免 params 数组分配）。
    private static int JsonObjectHasKeyCore(byte[] data, string key0, string? key1, string? key2)
    {
        if (data.Length == 0)
        {
            return -1;
        }

        try
        {
            var dec = new JsonDecoder(data);
            if (dec.PeekKind() != JsonKind.BeginObject)
            {
                return -1;
            }

            dec.ReadToken();
            while (dec.PeekKind() != JsonKind.EndObject)
            {
                var name = dec.ReadValue();
                if (JsonKeyCheck(name, key0))
                {
                    return 0;
                }

                if (key1 is not null && JsonKeyCheck(name, key1))
                {
                    return 1;
                }

                if (key2 is not null && JsonKeyCheck(name, key2))
                {
                    return 2;
                }

                dec.SkipValue();
            }

            return -1;
        }
        catch (JsonException)
        {
            return -1;
        }
    }

    // ---- Go 的 lsp.go RequestMessage.UnmarshalParams ------------------------------------------

    /// <summary>Go: <c>func (r *RequestMessage) UnmarshalParams[T any]() (T, error)</c>（lsp.go:235）：
    /// 入站消息的 params 存的是原始 [json.Value]，解码推迟到分发点（未被服务处理的方法的类型不会进二进制）。
    /// NoParams 方法必须没有 params；其余方法必须是 object 或 array；违规抛
    /// <c>ErrorCodeException(ErrorCode.InvalidParams, …)</c>。</summary>
    public static T UnmarshalParams<T>(this RequestMessage r)
        where T : new()
    {
        byte[]? raw = null;
        if (r.Params is not null)
        {
            if (r.Params is not byte[] v)
            {
                throw new ErrorCodeException(ErrorCode.InvalidParams, $"{ErrorCode.InvalidParams.String()}: unexpected params type {r.Params.GetType()}");
            }

            raw = v;
        }

        // params 是 T 的零值；这里断言它的类型，也就是该方法是否声明成 NoParams。
        if (typeof(T) == typeof(NoParams))
        {
            if (raw is not null && raw.Length != 0)
            {
                throw new ErrorCodeException(ErrorCode.InvalidParams, $"{ErrorCode.InvalidParams.String()}: expected no params, got {Encoding.UTF8.GetString(raw)}");
            }

            return new T();
        }

        // 基础协议把 params 定义为 `array | object`；其余（缺失、null、标量）一律拒绝。
        if (raw is null || raw.Length == 0)
        {
            throw new ErrorCodeException(ErrorCode.InvalidParams, $"{ErrorCode.InvalidParams.String()}: params must be an object or array");
        }

        var decoder = new JsonDecoder(raw);
        var kind = decoder.PeekKind();
        if (kind != JsonKind.BeginObject && kind != JsonKind.BeginArray)
        {
            throw new ErrorCodeException(ErrorCode.InvalidParams, $"{ErrorCode.InvalidParams.String()}: params must be an object or array");
        }

        try
        {
            return UnmarshalDecodeNew<T>(decoder);
        }
        catch (JsonException e)
        {
            throw new ErrorCodeException(ErrorCode.InvalidParams, $"{ErrorCode.InvalidParams.String()}: {e.Message}", e);
        }
    }

    // ---- Go 的 lsp.go 客户端能力（context value → AsyncLocal）---------------------------------

    private static readonly AsyncLocal<ResolvedClientCapabilities?> _clientCapabilities = new();

    /// <summary>Go: <c>func WithClientCapabilities(ctx context.Context, caps *ResolvedClientCapabilities) context.Context</c>（lsp.go:283）。
    /// C# 侧没有 context value：值存在 AsyncLocal 里，随当前 async 流传播（等价于派生的 ctx）。
    /// **必须在每个请求自己的 async 流里调用**（Go 是在 handleRequestOrNotification 开头派生 ctx）——
    /// 放在共享的 read loop 里设会串到其它请求（AsyncLocal 出作用域不会自动还原，子流的写入也不回流到父）。</summary>
    public static void WithClientCapabilities(ResolvedClientCapabilities caps) => _clientCapabilities.Value = caps;

    /// <summary>Go: <c>func GetClientCapabilities(ctx context.Context) *ResolvedClientCapabilities</c>（lsp.go:287）：
    /// 没设置过就返回全零的 ResolvedClientCapabilities。</summary>
    public static ResolvedClientCapabilities GetClientCapabilities() => _clientCapabilities.Value ?? new ResolvedClientCapabilities();

    /// <summary>Go: <c>func PreferredMarkupKind(formats []MarkupKind) MarkupKind</c>（lsp.go:298）：
    /// 取第一个（最优先）；空切片返回 MarkupKindPlainText。</summary>
    public static MarkupKind PreferredMarkupKind(MarkupKind[]? formats) => formats is { Length: > 0 } ? formats[0] : MarkupKind.PlainText;
}

/// <summary>Go 的 CodeActionKind 常量（lsp.go:305-309，不在 lsp_generated.go 里）与 <c>Contains</c>。
/// 类型本体在 Lsp.Generated.Enums.cs（生成器对命名 string 类型发 partial）。</summary>
public readonly partial struct CodeActionKind
{
    /// <summary>Go: <c>CodeActionKindSourceFixAllTs CodeActionKind = CodeActionKindSourceFixAll + ".ts"</c>（lsp.go:306）。</summary>
    public static readonly CodeActionKind SourceFixAllTs = new("source.fixAll.ts");

    /// <summary>Go: <c>CodeActionKindSourceOrganizeImportsTs CodeActionKind = CodeActionKindSourceOrganizeImports + ".ts"</c>（lsp.go:307）。</summary>
    public static readonly CodeActionKind SourceOrganizeImportsTs = new("source.organizeImports.ts");

    /// <summary>Go: <c>CodeActionKindSourceRemoveUnusedImportsTs CodeActionKind = CodeActionKindSource + ".removeUnusedImports.ts"</c>（lsp.go:308）。</summary>
    public static readonly CodeActionKind SourceRemoveUnusedImportsTs = new("source.removeUnusedImports.ts");

    /// <summary>Go: <c>CodeActionKindSourceSortImportsTs CodeActionKind = CodeActionKindSource + ".sortImports.ts"</c>（lsp.go:309）。</summary>
    public static readonly CodeActionKind SourceSortImportsTs = new("source.sortImports.ts");

    /// <summary>Go: <c>func (kind CodeActionKind) Contains(other CodeActionKind) bool</c>（lsp.go:301）：
    /// other 是 kind 或它的子级。</summary>
    public bool Contains(CodeActionKind other) =>
        Equals(other) || this == Empty || other.Value.StartsWith(Value + ".", StringComparison.Ordinal);
}
