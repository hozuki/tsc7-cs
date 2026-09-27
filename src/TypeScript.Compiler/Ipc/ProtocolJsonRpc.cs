// ported from: internal/ipc/protocol_jsonrpc.go
//
// C# 映射与偏离（逐条）：
// 1) Go 的 `json.Marshal(msg)`（RequestMessage/ResponseMessage 自带 MarshalerTo）→ C# 直接复用协调者已落地的
//    `TypeScript.JsonRpc.RequestMessage`/`ResponseMessage` + `Json.Marshal`，字段顺序/省略规则与 Go 逐条一致。
// 2) **原始 JSON 的写出**：Go 里 `params`/`result` 可能是 `json.Value`（实现 json.Marshaler，原样写出）。
//    C# 的对应物是 `byte[]`，而本项目 Json 包把 `byte[]` 当 base64（docs/json.md §2）。所以这里用内部包装类
//    `RawJsonValue` 把 byte[] 包成 `IJsonMarshaler`（`Encoder.WriteValue`），保证原样输出。
//    **空 byte[] 视为"无值"**（Go 的 jsontext 对空 Value 会报错；本仓库既有口径是"nil 与空都省略"，
//    见 JsonRpc.cs 的 WriteRawMember）。`WriteResponse(id, null)` 按 Go 写 `"result": null`。
// 3) Go 的 `io.ReadWriter` → C# 的 `Stream`；`jsonrpc.Reader` 读到流结尾抛 `EndOfStreamException`，
//    本方法按 `Protocol` 契约把它翻译成 **null**（Go 的 io.EOF）。
using TypeScript.Json;
using TypeScript.JsonRpc;

using static TypeScript.Json.Json;

namespace TypeScript.Ipc;

/// <summary>
/// Go: <c>type JSONRPCProtocol struct</c>（JSON-RPC 2.0 + LSP 的 Content-Length 分帧）。
/// </summary>
public sealed class JSONRPCProtocol : Protocol
{
    /// <summary>Go: <c>jsonrpc.NewReader(rw)</c>。</summary>
    private readonly Reader _reader;

    /// <summary>Go: <c>jsonrpc.NewWriter(rw)</c>。</summary>
    private readonly Writer _writer;

    /// <summary>共享的 <c>"result": null</c> 原始值（Go 的 `json.Value("null")`）。</summary>
    private static readonly RawJsonValue _nullJson = new("null"u8.ToArray());

    private JSONRPCProtocol(Stream rw)
    {
        _reader = Reader.NewReader(rw);
        _writer = Writer.NewWriter(rw);
    }

    /// <summary>Go: <c>func NewJSONRPCProtocol(rw io.ReadWriter) *JSONRPCProtocol</c>。</summary>
    public static JSONRPCProtocol NewJSONRPCProtocol(Stream rw) => new(rw);

    /// <summary>Go: <c>func (p *JSONRPCProtocol) ReadMessage() (*Message, error)</c>。</summary>
    public Message? ReadMessage()
    {
        byte[] data;
        try
        {
            data = _reader.Read();
        }
        catch (EndOfStreamException)
        {
            // Go: io.EOF 由调用方（连接）判成"正常结束"；C# 侧统一成 null（见 Protocol.cs）。
            return null;
        }

        return UnmarshalNew<Message>(data);
    }

    /// <summary>Go: <c>func (p *JSONRPCProtocol) WriteRequest(id *jsonrpc.ID, method string, params any) error</c>。</summary>
    public void WriteRequest(ID? id, string method, object? @params)
    {
        var msg = new RequestMessage { ID = id, Method = method, Params = WrapRaw(@params) };
        _writer.Write(Marshal(msg));
    }

    /// <summary>Go: <c>func (p *JSONRPCProtocol) WriteNotification(method string, params any) error</c>。</summary>
    public void WriteNotification(string method, object? @params)
    {
        var msg = new RequestMessage { Method = method, Params = WrapRaw(@params) };
        _writer.Write(Marshal(msg));
    }

    /// <summary>Go: <c>func (p *JSONRPCProtocol) WriteResponse(id *jsonrpc.ID, result any) error</c>。</summary>
    public void WriteResponse(ID? id, object? result)
    {
        // Go: `if result == nil { result = json.Value("null") }`
        var msg = new ResponseMessage { ID = id, Result = result is null ? _nullJson : WrapRaw(result) };
        _writer.Write(Marshal(msg));
    }

    /// <summary>Go: <c>func (p *JSONRPCProtocol) WriteError(id *jsonrpc.ID, respErr *jsonrpc.ResponseError) error</c>。</summary>
    public void WriteError(ID? id, ResponseError? err)
    {
        var msg = new ResponseMessage { ID = id, Error = err };
        _writer.Write(Marshal(msg));
    }

    /// <summary>Go 的 <c>json.Value</c>（= C# 的 <c>byte[]</c>）在 params/result 位置上要原样写出。</summary>
    private static object? WrapRaw(object? value)
    {
        if (value is not byte[] raw)
        {
            return value;
        }

        return raw.Length == 0 ? null : new RawJsonValue(raw);
    }

    /// <summary>
    /// 把原始 JSON 字节按 <c>Encoder.WriteValue</c> 写出（Go 的 <c>json.Value</c> 的 MarshalerTo）。
    /// 直接交给 Json.Marshal 会被当成 base64（docs/json.md §2）。
    /// </summary>
    private sealed class RawJsonValue(byte[] value) : IJsonMarshaler
    {
        public void MarshalJSONTo(Encoder encoder) => encoder.WriteValue(value);
    }
}
