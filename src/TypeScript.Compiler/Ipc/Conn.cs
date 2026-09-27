// ported from: internal/ipc/conn.go
//
// C# 映射与偏离（逐条）：
// 1) Go 的 `(T, error)` → C#：正常路径返回 T，错误路径抛异常（docs/architecture.md §3.2）。
// 2) Go 的 `var ErrConnClosed = errors.New("ipc: connection closed")` → `ConnClosedException`
//    （Go 的 `errors.Is(err, ErrConnClosed)` → C# 的 `catch (ConnClosedException)`）；
//    `ErrRequestTimeout` → `RequestTimeoutException`（Go 侧声明了但当前没有任何调用点，C# 保留同样形状）。
// 3) Go 的 `fmt.Errorf("…: %w", err)` → 本文件的 `IpcException`（前缀文本 + InnerException 保留原始异常，
//    于是 Go 里 `errors.Is(err, err)` 的语义在 C# 里是"沿 InnerException 链找同一个实例"）。
//    Go 的 `errors.Join(a, b)` 也按这条落地：文本用 "\n" 连接，见 ConnAsync.JoinErrors。
// 4) Go 的 `context.Context` → `TypeScript.Core.Context`（本文件里裸写 `Context`）；Go 的 `ctx.Err()` →
//    `OperationCanceledException`（C# 的 Context 只带 CancellationToken，区分不了 Canceled/DeadlineExceeded）。
// 5) Go 的 `json.Value`（= jsontext.Value，本质 []byte 的原始 JSON）→ C# 的 `byte[]`；原始 JSON **只能**走
//    `Encoder.WriteValue`/`Decoder.ReadValue`，绝不交给 `Json.Marshal`（本项目 Json 包把 byte[] 当 base64）。
// 6) Go 的 `UnmarshalParams[T any]` 是包级泛型函数 → 静态类 `Ipc` 的静态方法（architecture.md §2）。
//    `params` 为 nil/空时 Go 返回 `(nil, nil)` 指针 → C# 返回 `default`（引用类型即 null）。
// 7) `Handler.HandleRequest` 在 Go 里返回 `(any, error)`：C# 侧"返回错误"= 抛**非** `DebugFailureException`
//    的异常；Go 的 `panic`/`debug.Fail` = 抛 `DebugFailureException`（§3.2 "panic → Debug.Fail"）。
//    连接靠这个类型区分两条路径（见 ConnSync/ConnAsync 的 HandleRequest 注释）。
using TypeScript.Core;

using static TypeScript.Json.Json;

namespace TypeScript.Ipc;

/// <summary>Go 的 <c>var ErrConnClosed</c>。</summary>
public sealed class ConnClosedException : Exception
{
    public ConnClosedException()
        : base("ipc: connection closed")
    {
    }

    /// <summary>Go: <c>errors.Join(ErrConnClosed, cause)</c>。文本按 Go 的规则用 <c>"\n"</c> 连接，
    /// <c>cause</c> 收进 <see cref="Exception.InnerException"/>（等价 Go 里 <c>errors.Is(err, cause)</c> 的查询）。</summary>
    internal ConnClosedException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Go 的 <c>var ErrRequestTimeout</c>。</summary>
public sealed class RequestTimeoutException() : Exception("ipc: request timeout");

/// <summary>
/// Go 的 <c>fmt.Errorf("ipc: …: %w", err)</c>。ipc 包内的每一处错误包装都是这个形状：
/// 文本是 Go 的格式串结果，原始异常留在 <see cref="Exception.InnerException"/>。
/// </summary>
public sealed class IpcException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>Go: <c>type Handler interface</c>。请求/通知的处理者（API 会话、content mapper…）。</summary>
public interface Handler
{
    /// <summary>
    /// Go: <c>HandleRequest(ctx, method, params) (any, error)</c>。返回结果；失败抛异常
    /// （抛 <c>DebugFailureException</c> 表示 Go 的 panic，见文件头第 7 条）。
    /// <paramref name="params"/> 是原始 JSON 字节（Go 的 <c>json.Value</c>），可能为 null。
    /// </summary>
    object? HandleRequest(Context ctx, string method, byte[]? @params);

    /// <summary>Go: <c>HandleNotification(ctx, method, params) error</c>（Go 侧返回值被忽略）。</summary>
    void HandleNotification(Context ctx, string method, byte[]? @params);
}

/// <summary>Go: <c>type Conn interface</c>。双向 API 连接。</summary>
public interface Conn
{
    /// <summary>
    /// Go: <c>Run(ctx) error</c>。阻塞处理消息，直到 ctx 取消（抛
    /// <see cref="OperationCanceledException"/>）、流正常结束（正常返回）或出错（抛异常）。
    /// </summary>
    void Run(Context ctx);

    /// <summary>Go: <c>Call(ctx, method, params) (json.Value, error)</c>。向对端发请求并等待响应；
    /// 返回原始 JSON 结果字节；失败抛异常。</summary>
    byte[]? Call(Context ctx, string method, object? @params);

    /// <summary>Go: <c>Notify(ctx, method, params) error</c>。发通知（不等响应）。</summary>
    void Notify(Context ctx, string method, object? @params);
}

/// <summary>
/// Go 的 <c>ipc</c> 包级函数/常量的家（C# 没有自由函数，见 architecture.md §2）。
/// 本类分多个 partial 分片：<c>conn.go</c> 的 <c>UnmarshalParams</c> 在这里，
/// <c>timing.go</c> 的两个方法名常量在 Timing.cs，<c>transport_{unix,windows}.go</c> 的
/// <c>GeneratePipePath</c> 在 Transport.cs。
/// </summary>
public static partial class Ipc
{
    /// <summary>
    /// Go: <c>func UnmarshalParams[T any](params json.Value) (*T, error)</c>。
    /// 空/nil 时 Go 返回 nil 指针 → C# 返回 <c>default</c>（引用类型即 null；值类型是零值，见文件头第 6 条）。
    /// </summary>
    public static T? UnmarshalParams<T>(byte[]? @params)
        where T : new()
    {
        if (@params is null || @params.Length == 0)
        {
            return default;
        }

        return UnmarshalNew<T>(@params);
    }
}
