// ported from: internal/ipc/conn_sync.go
//
// C# 映射与偏离（逐条）：
// 1) Go 的 `sync.Mutex` + 显式 Lock/Unlock（Call 里处理嵌套请求时**临时解锁**）→ C# 的
//    `Monitor.Enter/Exit`（不能用 `lock` 语句：Go 在循环中途解锁再上锁，作用域记法表达不了）。
//    加锁范围与 Go 逐条一致（读/写协议期间持锁，处理嵌套请求期间放锁）。
// 2) Go 的 `io.ReadWriteCloser` → C# 的 `Stream`；`Protocol.ReadMessage()` 返回 null 表示 Go 的 io.EOF
//    （Protocol.cs 契约），于是 `Run` 里"读到 EOF 即正常返回"。
// 3) Go 的 `(T, error)` → 抛异常；`ctx.Err()` → `OperationCanceledException`。
// 4) Go 的 `fmt.Errorf("…: %w", err)` → `IpcException`（前缀文本 + InnerException，见 Conn.cs 文件头第 3 条）。
// 5) Go 的 panic/recover（defer 里 `debug.Stack()`）→ C# 的 `catch (DebugFailureException)`（§3.2 的映射）
//    + `Exception.ToString()` 代替 goroutine 栈（ticket 指定）。
// 6) `time.Now()`/`time.Since(start)` → `Stopwatch.GetTimestamp()`/`Stopwatch.GetElapsedTime`；
//    `time.Duration` → `TimeSpan`（Timing.cs 文件头第 2 条）。
// 7) **保留但未使用**：Go 的 SyncConn 存了 `rwc` 却从不使用（连接关不关是调用方的事），C# 同样保留
//    （`internal` 字段，不产生 CS0414）。
using Stopwatch = System.Diagnostics.Stopwatch;
using TypeScript.Core;
using TypeScript.JsonRpc;

namespace TypeScript.Ipc;

/// <summary>
/// Go: <c>type SyncConn struct</c>。同步处理请求（一次一个、就地处理），出站调用串行化。
/// </summary>
public sealed class SyncConn : Conn
{
    /// <summary>Go: <c>rwc io.ReadWriteCloser</c>（Go 侧存而不用；见文件头第 7 条）。</summary>
    internal readonly Stream? _rwc;

    /// <summary>Go: <c>protocol Protocol</c>。</summary>
    private readonly Protocol _protocol;

    /// <summary>Go: <c>handler Handler</c>。</summary>
    private readonly Handler _handler;

    /// <summary>Go: <c>timing *timingCollector</c>（非 null 时累计每次请求的墙钟耗时）。</summary>
    private TimingCollector? _timing;

    /// <summary>Go: <c>mu sync.Mutex</c>（串行化所有协议操作：读与写成对原子）。</summary>
    private readonly object _mu = new();

    private SyncConn(Stream? rwc, Protocol protocol, Handler handler)
    {
        _rwc = rwc;
        _protocol = protocol;
        _handler = handler;
    }

    /// <summary>Go: <c>func NewSyncConn(rwc io.ReadWriteCloser, protocol Protocol, handler Handler) *SyncConn</c>。</summary>
    public static SyncConn NewSyncConn(Stream? rwc, Protocol protocol, Handler handler) => new(rwc, protocol, handler);

    /// <summary>Go: <c>func (c *SyncConn) SetCollectTiming(enabled bool)</c>。</summary>
    public void SetCollectTiming(bool enabled)
    {
        _timing = enabled ? new TimingCollector() : null;
    }

    /// <summary>Go: <c>func (c *SyncConn) Run(ctx context.Context) error</c>。</summary>
    public void Run(Context ctx)
    {
        while (true)
        {
            if (ctx.CancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(ctx.CancellationToken);
            }

            Message? msg;
            Monitor.Enter(_mu);
            try
            {
                msg = _protocol.ReadMessage();
            }
            finally
            {
                Monitor.Exit(_mu);
            }

            // Go: io.EOF → return nil（正常结束；Protocol 把它翻译成 null，见 Protocol.cs）。
            if (msg is null)
            {
                return;
            }

            if (msg.IsRequest())
            {
                HandleRequest(ctx, msg);
            }
            else if (msg.IsNotification())
            {
                HandleNotification(ctx, msg);
            }
            else
            {
                // Go: `errors.New("ipc: unexpected response message in sync connection")`。
                // 响应只该由 Call() 内联读取，不会出现在主循环里。
                throw new IpcException("ipc: unexpected response message in sync connection");
            }
        }
    }

    /// <summary>Go: <c>func (c *SyncConn) handleRequest(ctx context.Context, msg *Message) (retErr error)</c>。</summary>
    private void HandleRequest(Context ctx, Message msg)
    {
        // 元请求在派发给 handler 之前拦截，直接答复，且它们自己不计入耗时统计。
        switch (msg.Method)
        {
            case Ipc.MethodGetServerTiming:
            {
                Exception? timingWriteErr = null;
                lock (_mu)
                {
                    try
                    {
                        _protocol.WriteResponse(msg.ID, Ipc.ServerTimingSnapshot(_timing));
                    }
                    catch (Exception e)
                    {
                        timingWriteErr = e;
                    }
                }

                if (timingWriteErr is not null)
                {
                    throw new IpcException(
                        $"ipc: failed to write server timing response: {timingWriteErr.Message}",
                        timingWriteErr);
                }

                return;
            }

            case Ipc.MethodResetServerTiming:
            {
                _timing?.Reset();

                Exception? resetWriteErr = null;
                lock (_mu)
                {
                    try
                    {
                        _protocol.WriteResponse(msg.ID, null);
                    }
                    catch (Exception e)
                    {
                        resetWriteErr = e;
                    }
                }

                if (resetWriteErr is not null)
                {
                    throw new IpcException(
                        $"ipc: failed to write reset server timing response: {resetWriteErr.Message}",
                        resetWriteErr);
                }

                return;
            }
        }

        var start = _timing is not null ? Stopwatch.GetTimestamp() : 0L;

        object? result;
        Exception? handlerError = null;
        try
        {
            result = _handler.HandleRequest(ctx, msg.Method, msg.Params);
        }
        catch (DebugFailureException panic)
        {
            // Go 的 defer/recover：panic → 带栈的错误响应（C# 用 Exception.ToString() 代替 debug.Stack()）。
            HandlePanic(msg, panic);
            return;
        }
        catch (Exception e)
        {
            // Go 的 `(any, error)` 的错误分支（C# 里 handler 用抛异常表达"返回错误"）。
            result = null;
            handlerError = e;
        }

        if (_timing is not null)
        {
            _timing.Record(msg.Method, Stopwatch.GetElapsedTime(start));
        }

        Exception? writeErr = null;
        lock (_mu)
        {
            try
            {
                if (handlerError is not null)
                {
                    _protocol.WriteError(
                        msg.ID,
                        new ResponseError { Code = ResponseError.CodeInternalError, Message = handlerError.Message });
                }
                else
                {
                    _protocol.WriteResponse(msg.ID, result);
                }
            }
            catch (Exception e)
            {
                writeErr = e;
            }
        }

        if (writeErr is not null)
        {
            throw new IpcException($"ipc: failed to write response: {writeErr.Message}", writeErr);
        }
    }

    /// <summary>Go 的 recover 分支：写 panic 错误响应；写失败时错误里带上原始 panic 文本。</summary>
    private void HandlePanic(Message msg, DebugFailureException panic)
    {
        var panicError = new IpcException($"panic: {panic.Message}\n{panic}");

        Exception? writeErr = null;
        lock (_mu)
        {
            try
            {
                _protocol.WriteError(
                    msg.ID,
                    new ResponseError { Code = ResponseError.CodeInternalError, Message = panicError.Message });
            }
            catch (Exception e)
            {
                writeErr = e;
            }
        }

        if (writeErr is not null)
        {
            throw new IpcException(
                $"ipc: failed to write panic error response: {writeErr.Message} (original panic: {panic.Message})",
                writeErr);
        }
    }

    /// <summary>Go: <c>func (c *SyncConn) handleNotification(ctx, msg)</c>（Go 侧忽略 handler 的错误）。</summary>
    private void HandleNotification(Context ctx, Message msg)
    {
        // Go: `_ = c.handler.HandleNotification(...)`——返回值被丢弃；通知是单向的，没有错误响应可发。
        // C# 里 handler 用抛异常表达错误，这里同样丢弃（否则 Task/线程边界上的未观测异常会打断连接）。
        try
        {
            _handler.HandleNotification(ctx, msg.Method, msg.Params);
        }
        catch (Exception)
        {
            // 与 Go 的 `_ =` 一致：通知处理失败不影响连接。
        }
    }

    /// <summary>Go: <c>func (c *SyncConn) Call(ctx, method string, params any) (json.Value, error)</c>。</summary>
    public byte[]? Call(Context ctx, string method, object? @params)
    {
        // 串行化所有 Call：msgpack 协议用方法名当响应 id，且 handler 内部的 goroutine 可能并发发起回调，
        // 必须保证"写请求 + 读响应"成对原子。
        Monitor.Enter(_mu);
        try
        {
            var id = ID.NewIDString(method);

            _protocol.WriteRequest(id, method, @params);

            if (ctx.CancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(ctx.CancellationToken);
            }

            while (true)
            {
                // 内联读响应。
                var msg = _protocol.ReadMessage();
                if (msg is null)
                {
                    // Go: 直接返回 io.EOF 错误 → C# 的 EndOfStreamException。
                    throw new EndOfStreamException();
                }

                if (msg.IsResponse() && msg.ID is not null && msg.ID.String() == method)
                {
                    if (msg.Error is not null)
                    {
                        throw new IpcException($"ipc: remote error [{msg.Error.Code}]: {msg.Error.Message}");
                    }

                    return msg.Result;
                }

                if (msg.IsRequest())
                {
                    // 同步客户端回调可能发起嵌套 API 请求：处理期间释放协议锁，让嵌套回调得以进行。
                    Monitor.Exit(_mu);
                    try
                    {
                        HandleRequest(ctx, msg);
                    }
                    finally
                    {
                        Monitor.Enter(_mu);
                    }

                    continue;
                }

                if (msg.IsNotification())
                {
                    Monitor.Exit(_mu);
                    try
                    {
                        HandleNotification(ctx, msg);
                    }
                    finally
                    {
                        Monitor.Enter(_mu);
                    }

                    continue;
                }

                throw new IpcException($"ipc: unexpected message while waiting for \"{method}\" response");
            }
        }
        finally
        {
            Monitor.Exit(_mu);
        }
    }

    /// <summary>Go: <c>func (c *SyncConn) Notify(ctx, method string, params any) error</c>。</summary>
    public void Notify(Context ctx, string method, object? @params)
    {
        lock (_mu)
        {
            _protocol.WriteNotification(method, @params);
        }
    }
}
