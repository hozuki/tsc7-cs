// ported from: internal/api/protocol_msgpack.go:1-281（整文件）
//
// 与 Go 的偏离（逐条说明）：
//
// 1) `type MessageType uint8` + `iota` 常量 → C# `enum MessageType : byte`，成员名 = Go 常量名去掉 `MessageType`
//    前缀（本仓 §3.3 的枚举命名规则），**数值与 Go 一致**（0..6）。Go 的 `IsValid()` → `MessageTypeExtensions.IsValid`
//    扩展方法（C# 的 enum 不能带实例方法）。
//
// 2) Go 的 `func (i MessageType) String()`（`stringer_generated.go`）→ 静态类 `Api` 里的扩展方法
//    `String(this MessageType)`（本仓 §3.3：C# enum 不能覆写 ToString，Go 的 stringer 落成扩展方法），
//    内容由 `artifacts/api-codegen/gen-stringer.mjs` 从 Go 的 `_MessageType_name`/`_MessageType_index` 确定性生成
//    → `MessageType.Generated.cs`。
//
// 3) Go 的 `bufio.Reader`/`bufio.Writer`（写侧必须显式 Flush）→ 两个 `BufferedStream`：
//    `writeTuple` 末尾的 `p.w.Flush()` 逐字对应 `BufferedStream.Flush()`。
//
// 4) `io.ReadWriter` → `Stream`；Go 的 `p.r.ReadByte()` 在流尾返回 `io.EOF`，本文件把它统一成
//    `System.IO.EndOfStreamException`，再由 `ReadMessage` 翻成 **null**（`Ipc.Protocol` 的既定契约，见 Ipc/Protocol.cs）。
//    Go 的 `io.ReadFull` → `ReadFull`（同样抛 `EndOfStreamException`）。
//
// 5) Go 的 `(T, error)` → 抛异常（`InvalidRequestException` 对应 `ErrInvalidRequest` 的 `%w` 包装，
//    文本逐字保留 Go 的格式串）。Go 的 `fmt.Errorf("%w: expected fixed 3-element array (0x93), received: 0x%02x", …)`
//    → 同文本的 `InvalidRequestException`。
//
// 6) `json.Marshal(params)` → `TypeScript.Json.Json.Marshal(params)`（本仓 Json 包；见 docs/json.md）。
//    `RawBinary` 的 `result.(RawBinary)` 类型断言 → `result is RawBinary raw`（`RawBinary.Data` 即 Go 的切片）。
//
// 7) Go 的 `binary.Read(p.r, binary.BigEndian, &sizeN)` → 逐个字节移位组装（C# 的 `BinaryPrimitives` 需要
//    `Span<byte>`，这里直接读字节更贴近 Go 的可读性，且没有分配）。
using System.Buffers.Binary;
using System.Text;
using TypeScript.Json;
using TypeScript.JsonRpc;
using static TypeScript.Json.Json;

namespace TypeScript.Api;

/// <summary>Go: <c>type MessageType uint8</c>（protocol_msgpack.go:20）。数值与 Go 的 iota 一致。</summary>
public enum MessageType : byte
{
    Unknown = 0,
    Request = 1,
    CallResponse = 2,
    CallError = 3,
    Response = 4,
    Error = 5,
    Call = 6,
}

/// <summary>Go: <c>func (m MessageType) IsValid() bool</c>（protocol_msgpack.go:32-34）。</summary>
public static class MessageTypeExtensions
{
    /// <summary>Go: <c>m &gt;= MessageTypeRequest &amp;&amp; m &lt;= MessageTypeCall</c>。</summary>
    public static bool IsValid(this MessageType m) => m is >= MessageType.Request and <= MessageType.Call;
}

/// <summary>
/// Go: <c>type MessagePackProtocol struct</c>（protocol_msgpack.go:47-50）：自定义 msgpack 元组格式
/// <c>[MessageType, method, payload]</c> 的协议实现。
/// </summary>
public sealed class MessagePackProtocol : Ipc.Protocol
{
    /// <summary>Go: <c>msgpackFixedArray3 byte = 0x93</c>。</summary>
    internal const int MsgpackFixedArray3 = 0x93;

    /// <summary>Go: <c>msgpackBin8 byte = 0xC4</c>。</summary>
    internal const int MsgpackBin8 = 0xC4;

    /// <summary>Go: <c>msgpackBin16 byte = 0xC5</c>。</summary>
    internal const int MsgpackBin16 = 0xC5;

    /// <summary>Go: <c>msgpackBin32 byte = 0xC6</c>。</summary>
    internal const int MsgpackBin32 = 0xC6;

    /// <summary>Go: <c>msgpackU8 byte = 0xCC</c>。</summary>
    internal const int MsgpackU8 = 0xCC;

    /// <summary>Go: <c>r *bufio.Reader</c>。</summary>
    private readonly BufferedStream _r;

    /// <summary>Go: <c>w *bufio.Writer</c>。</summary>
    private readonly BufferedStream _w;

    private MessagePackProtocol(Stream rw)
    {
        _r = new BufferedStream(rw);
        _w = new BufferedStream(rw);
    }

    /// <summary>Go: <c>func NewMessagePackProtocol(rw io.ReadWriter) *MessagePackProtocol</c>（protocol_msgpack.go:53-58）。</summary>
    public static MessagePackProtocol NewMessagePackProtocol(Stream rw) => new(rw);

    /// <summary>Go: <c>func (p *MessagePackProtocol) ReadMessage() (*ipc.Message, error)</c>（protocol_msgpack.go:61-96）。</summary>
    public Message? ReadMessage()
    {
        MessageType msgType;
        string method;
        byte[] payload;
        try
        {
            (msgType, method, payload) = ReadTuple();
        }
        catch (EndOfStreamException)
        {
            // Go: 流尾的 io.EOF 由连接层判成"正常结束"；C# 侧协议层统一翻成 null（见文件头第 4 条）。
            return null;
        }

        // Convert msgpack message type to JSON-RPC message
        var msg = new Message();

        switch (msgType)
        {
            case MessageType.Request:
                // Client request - needs an ID for response
                // We use the method as a pseudo-ID since this protocol doesn't have explicit IDs
                msg.ID = ID.NewIDString(method);
                msg.Method = method;
                msg.Params = payload;
                break;
            case MessageType.CallResponse:
                // Response to our Call - use method as ID
                // Note: Method must be empty for IsResponse() to return true
                msg.ID = ID.NewIDString(method);
                msg.Result = payload;
                break;
            case MessageType.CallError:
                // Error response to our Call
                msg.ID = ID.NewIDString(method);
                msg.Error = new ResponseError
                {
                    Code = ResponseError.CodeInternalError,
                    Message = Encoding.UTF8.GetString(payload),
                };
                break;
            default:
                throw new InvalidRequestException($"unexpected message type: {(byte)msgType}");
        }

        return msg;
    }

    /// <summary>Go: <c>func (p *MessagePackProtocol) readTuple() (MessageType, string, []byte, error)</c>（protocol_msgpack.go:98-152）。</summary>
    private (MessageType MsgType, string Method, byte[] Payload) ReadTuple()
    {
        // Read fixed array marker (0x93 = 3-element array)
        var t = ReadByte();
        if (t != MsgpackFixedArray3)
        {
            throw new InvalidRequestException(
                $"api: invalid request: expected fixed 3-element array (0x93), received: 0x{t:x2}");
        }

        // Read message type - can be positive fixint (0x00-0x7F) or uint8 (0xCC + value)
        t = ReadByte();
        byte rawType;
        if (t <= 0x7F)
        {
            // Positive fixint - the byte IS the value
            rawType = (byte)t;
        }
        else if (t == MsgpackU8)
        {
            // uint8 marker - next byte is the value
            rawType = (byte)ReadByte();
        }
        else
        {
            throw new InvalidRequestException(
                $"api: invalid request: expected positive fixint or uint8 marker, received: 0x{t:x2}");
        }

        var msgType = (MessageType)rawType;
        if (!msgType.IsValid())
        {
            throw new InvalidRequestException($"api: invalid request: unknown message type: {rawType}");
        }

        // Read method (binary)
        var methodBytes = ReadBin();
        var method = Encoding.UTF8.GetString(methodBytes);

        // Read payload (binary)
        var payload = ReadBin();

        return (msgType, method, payload);
    }

    /// <summary>Go: <c>func (p *MessagePackProtocol) readBin() ([]byte, error)</c>（protocol_msgpack.go:154-191）。</summary>
    private byte[] ReadBin()
    {
        var t = ReadByte();

        int size;
        switch (t)
        {
            case MsgpackBin8:
                size = ReadByte();
                break;
            case MsgpackBin16:
                size = (ReadByte() << 8) | ReadByte();
                break;
            case MsgpackBin32:
                size = (ReadByte() << 24) | (ReadByte() << 16) | (ReadByte() << 8) | ReadByte();
                break;
            default:
                throw new InvalidRequestException(
                    $"api: invalid request: expected binary data (0xc4-0xc6), received: 0x{t:x2}");
        }

        var payload = new byte[size];
        ReadFull(payload);
        return payload;
    }

    /// <summary>Go: <c>func (p *MessagePackProtocol) WriteRequest(id *jsonrpc.ID, method string, params any) error</c>
    /// （protocol_msgpack.go:193-201）：msgpack 协议下服务端发出的请求是 "Call" 类型。</summary>
    public void WriteRequest(ID? id, string method, object? @params)
    {
        // For msgpack protocol, requests from server are "Call" type
        var payload = Marshal(@params);
        WriteTuple(MessageType.Call, method, payload);
    }

    /// <summary>Go: <c>func (p *MessagePackProtocol) WriteNotification(method string, params any) error</c>（protocol_msgpack.go:203-206）。</summary>
    public void WriteNotification(string method, object? @params)
    {
        // Msgpack protocol doesn't distinguish notifications from calls
        WriteRequest(null, method, @params);
    }

    /// <summary>Go: <c>func (p *MessagePackProtocol) WriteResponse(id *jsonrpc.ID, result any) error</c>（protocol_msgpack.go:208-232）。</summary>
    public void WriteResponse(ID? id, object? result)
    {
        var method = id is null ? "" : id.String();

        byte[] payload;

        // Check if result is raw binary (for efficient binary transport)
        if (result is RawBinary raw)
        {
            payload = raw.Data;
        }
        else
        {
            payload = Marshal(result);
        }

        WriteTuple(MessageType.Response, method, payload);
    }

    /// <summary>Go: <c>func (p *MessagePackProtocol) WriteError(id *jsonrpc.ID, respErr *jsonrpc.ResponseError) error</c>
    /// （protocol_msgpack.go:234-240）。</summary>
    public void WriteError(ID? id, ResponseError? err)
    {
        var method = id is null ? "" : id.String();
        WriteTuple(MessageType.Error, method, Encoding.UTF8.GetBytes(err!.Message));
    }

    /// <summary>Go: <c>func (p *MessagePackProtocol) writeTuple(msgType MessageType, method string, payload []byte) error</c>
    /// （protocol_msgpack.go:242-262）。</summary>
    private void WriteTuple(MessageType msgType, string method, byte[] payload)
    {
        // Write fixed array marker
        _w.WriteByte((byte)MsgpackFixedArray3);
        // Write message type as positive fixint (values 0-127 are written directly)
        _w.WriteByte((byte)msgType);
        // Write method
        WriteBin(Encoding.UTF8.GetBytes(method));
        // Write payload
        WriteBin(payload);
        _w.Flush();
    }

    /// <summary>Go: <c>func (p *MessagePackProtocol) writeBin(data []byte) error</c>（protocol_msgpack.go:264-281）。</summary>
    private void WriteBin(byte[] data)
    {
        var length = data.Length;
        if (length < 256)
        {
            _w.WriteByte((byte)MsgpackBin8);
            _w.WriteByte((byte)length);
        }
        else if (length < 1 << 16)
        {
            _w.WriteByte((byte)MsgpackBin16);
            Span<byte> size = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(size, (ushort)length);
            _w.Write(size);
        }
        else
        {
            _w.WriteByte((byte)MsgpackBin32);
            Span<byte> size = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(size, (uint)length);
            _w.Write(size);
        }

        _w.Write(data, 0, data.Length);
    }

    /// <summary>Go 的 <c>p.r.ReadByte()</c>：读一个字节，流尾抛 <see cref="EndOfStreamException"/>。</summary>
    private int ReadByte()
    {
        var b = _r.ReadByte();
        if (b < 0)
        {
            throw new EndOfStreamException();
        }

        return b;
    }

    /// <summary>Go 的 <c>io.ReadFull(p.r, payload)</c>：读满整段，流尾抛 <see cref="EndOfStreamException"/>。</summary>
    private void ReadFull(byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = _r.Read(buffer, offset, buffer.Length - offset);
            if (read <= 0)
            {
                throw new EndOfStreamException();
            }

            offset += read;
        }
    }
}
