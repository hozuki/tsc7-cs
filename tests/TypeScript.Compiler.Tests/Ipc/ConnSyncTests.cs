// ported from: internal/ipc/conn_sync_test.go
//
// 说明：
// - Go 的 `(T, error)` → C# 抛异常，所以 `assert.Assert(t, errors.Is(err, responseErr), …)` 写成
//   `IpcTestSupport.IsCausedBy(ex, responseErr)`（沿 InnerException 链找同一个异常实例；见 Ipc/Conn.cs 文件头）。
// - Go 的 `panic("handler panic")` → C# 抛 `DebugFailureException("handler panic")`（architecture.md §3.2 的
//   panic → Debug.Fail 映射；连接据此走 "panic" 分支并写带栈的错误响应）。
// - Go 的 `ipc.NewSyncConn(nil, protocol, …)` → `SyncConn.NewSyncConn(null, protocol, …)`（该用例不需要传输）。
// - Go 的 `errors.New("response write failed")` → C# 的异常实例（用 `InvalidOperationException` 承载同类文本）。
using TypeScript.Core;
using TypeScript.Ipc;
using TypeScript.JsonRpc;

using Xunit;

using static TypeScript.JsonRpc.JsonRpc;

namespace TypeScript.Tests;

public class ConnSyncTests
{
    [Fact]
    public void TestSyncConnRunReturnsResponseWriteFailure()
    {
        var responseErr = new InvalidOperationException("response write failed");
        var protocol = new SyncFailingResponseProtocol
        {
            _message = new Message { ID = NewIDInt(1), Method = "transform" },
            _responseErr = responseErr,
        };
        var conn = SyncConn.NewSyncConn(null, protocol, new NoOpHandler());

        var ex = Assert.Throws<IpcException>(() => conn.Run(Context.Background));
        Assert.Contains("ipc: failed to write response: response write failed", ex.Message);
        IpcTestSupport.IsCausedBy(ex, responseErr);
    }

    [Fact]
    public void TestSyncConnRunReturnsPanicResponseWriteFailure()
    {
        var responseErr = new InvalidOperationException("response write failed");
        var protocol = new SyncFailingResponseProtocol
        {
            _message = new Message { ID = NewIDInt(1), Method = "transform" },
            _responseErr = responseErr,
        };
        var conn = SyncConn.NewSyncConn(null, protocol, new PanicHandler());

        var ex = Assert.Throws<IpcException>(() => conn.Run(Context.Background));
        Assert.Contains(
            "ipc: failed to write panic error response: response write failed (original panic: handler panic)",
            ex.Message);
        IpcTestSupport.IsCausedBy(ex, responseErr);
    }
}

/// <summary>Go 测试文件里的 <c>syncFailingResponseProtocol</c>（只喂一条消息，读写响应一律失败）。</summary>
internal sealed class SyncFailingResponseProtocol : Protocol
{
    internal Message? _message;

    internal Exception _responseErr = new InvalidOperationException();

    public Message? ReadMessage()
    {
        // Go: 消息用完即报 io.EOF → C# 侧返回 null（Protocol.cs 契约）。
        var message = _message;
        _message = null;
        return message;
    }

    public void WriteRequest(ID? id, string method, object? @params)
    {
    }

    public void WriteNotification(string method, object? @params)
    {
    }

    public void WriteResponse(ID? id, object? result) => throw _responseErr;

    public void WriteError(ID? id, ResponseError? err) => throw _responseErr;
}

/// <summary>
/// Go 测试里的 <c>noOpHandler</c>（定义在 conn_async_test.go，conn_sync_test.go 也用它）：
/// HandleRequest 返回 (nil, nil)，HandleNotification 返回 nil。
/// </summary>
internal sealed class NoOpHandler : Handler
{
    public object? HandleRequest(Context ctx, string method, byte[]? @params) => null;

    public void HandleNotification(Context ctx, string method, byte[]? @params)
    {
    }
}

/// <summary>Go 测试文件里的 <c>panicHandler</c>。</summary>
internal sealed class PanicHandler : Handler
{
    public object? HandleRequest(Context ctx, string method, byte[]? @params) =>
        throw new DebugFailureException("handler panic");

    public void HandleNotification(Context ctx, string method, byte[]? @params)
    {
    }
}
