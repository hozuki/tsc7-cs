// ported from: internal/ipc/protocol.go
//
// C# 映射与偏离（逐条）：
// 1) Go 的 `type Message = jsonrpc.Message` 是**类型别名**；C# 没有公开别名 → 本包不新建
//    `TypeScript.Ipc.Message`，所有签名直接写 `TypeScript.JsonRpc.Message`（偏离，见报告）。
// 2) `Proto` 的方法在 Go 里返回 error → C# 抛异常；`ReadMessage` 的 `io.EOF` → C# 的 **null**
//    （与 `TypeScript.JsonRpc.Reader.Read` 抛 `EndOfStreamException` 的分工见 BaseProto.cs 文件头：
//    连接层把"正常读到流结尾"翻译成 null，其它错误照抛）。
// 3) Go 的 `params any`/`result any` → C# 的 `object?`；其中**原始 JSON 用 `byte[]`**（Go 的 `json.Value`），
//    JSONRPCProtocol 会把 byte[] 按 `Encoder.WriteValue` 原样写出，而不是当 base64（见 ProtocolJsonRpc.cs）。
using TypeScript.JsonRpc;

namespace TypeScript.Ipc;

/// <summary>Go: <c>type Protocol interface</c>。读/写 API 消息的协议层。</summary>
public interface Protocol
{
    /// <summary>Go: <c>ReadMessage() (*Message, error)</c>。读到流结尾（Go 的 <c>io.EOF</c>）返回 <c>null</c>。</summary>
    Message? ReadMessage();

    /// <summary>Go: <c>WriteRequest(id, method, params) error</c>。<c>id</c> 为 null 即通知形式的请求头。</summary>
    void WriteRequest(ID? id, string method, object? @params);

    /// <summary>Go: <c>WriteNotification(method, params) error</c>（无 id）。</summary>
    void WriteNotification(string method, object? @params);

    /// <summary>Go: <c>WriteResponse(id, result) error</c>。<c>result</c> 为 null 时按 Go 写 <c>null</c>。</summary>
    void WriteResponse(ID? id, object? result);

    /// <summary>Go: <c>WriteError(id, err) error</c>。</summary>
    void WriteError(ID? id, ResponseError? err);
}
