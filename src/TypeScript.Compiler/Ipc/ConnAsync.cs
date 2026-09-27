// ported from: internal/ipc/conn_async.go
//
// C# 映射与偏离（逐条）：
// 1) Go 的 goroutine（`go func(){…}`）+ `sync.WaitGroup` → C# 的 `Task.Run` + `Core.WaitGroup`
//    （core/workgroup.go 的同款映射；不是 async/await，全部是阻塞式等待——ticket 要求）。
// 2) 响应通道 `chan *Message`（容量 1）+ `close(ch)` 表达"连接已终止"→ `PendingCall`：
//    内部 `TaskCompletionSource<Message?>`，收到响应是 `Complete(msg)`，"连接终止"是 `Close()`（结果 null）；
//    Go 的 `select { case <-ctx.Done(): …; case resp, ok := <-ch: … }` → C# 的 `Task.Wait(Timeout.Infinite, token)`
//    （token 取消抛 OperationCanceledException，与 Go 返回 ctx.Err() 对应）。
// 3) Go 的 `atomic.Int64` → `Interlocked.Increment(ref long)`。
// 4) Go 的 `sync.Mutex` → C# 的 `lock`；`pendingMu`/`writeMu` 两个锁与 Go 一一对应。
// 5) Go 的 `terminal error` / `hasCause`（`errors.Join(ErrConnClosed, cause)`）→ C# 的 `ConnClosedException`
//    （文本按 "\n" 连接，cause 在 InnerException；见 Conn.cs 文件头第 2/3 条）。
// 6) Go 的 `errors.Join(err, requestErr)` → 本文件的 `JoinErrors`（文本拼接 + InnerException）。
// 7) `log/discard`：Go 的 `_ = c.rwc.Close()`（忽略关闭错误）→ C# 的 `try { Close(); } catch { }`。
// 8) `context.WithCancel` 会保留 ctx 上的所有值（含 locale），而 `Core.WithCancellation` 会丢 Locale
//    → 这里用与 `Locale.WithLocale` 相同的内部构造器派生 handlerCtx（见 Core/Context.cs）。
// 9) **handler 的 "返回错误" 与 "panic"**：C# 的 `Handler` 用抛异常表达 Go 的 `error`，用
//    `DebugFailureException` 表达 Go 的 panic/debug.Fail（architecture.md §3.2）——两种异常走不同分支：
//    前者写普通错误响应（message 原样），后者写 "panic: …\n<stack>"（`Exception.ToString()` 代替
//    `debug.Stack()`）。
using Stopwatch = System.Diagnostics.Stopwatch;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using TypeScript.Core;
using TypeScript.JsonRpc;

namespace TypeScript.Ipc;

/// <summary>
/// Go: <c>type AsyncConn struct</c>。每个请求在自己的任务里处理（LSP 风格 JSON-RPC 的标准实现）。
/// </summary>
public sealed class AsyncConn : Conn
{
    /// <summary>Go: <c>rwc io.ReadWriteCloser</c>（请求处理出错时由读循环这一侧关掉它）。</summary>
    internal readonly Stream? _rwc;

    /// <summary>Go: <c>protocol Protocol</c>。</summary>
    private readonly Protocol _protocol;

    /// <summary>Go: <c>handler Handler</c>。</summary>
    private readonly Handler _handler;

    /// <summary>Go: <c>timing *timingCollector</c>。</summary>
    private TimingCollector? _timing;

    /// <summary>Go: <c>seq atomic.Int64</c>（server→client 请求的序号）。</summary>
    private long _seq;

    /// <summary>Go: <c>pending map[jsonrpc.ID]chan *Message</c>（ID 按值相等，正是 C# 的 ID.Equals）。</summary>
    private readonly Dictionary<ID, PendingCall> _pending = new();

    /// <summary>Go: <c>pendingMu sync.Mutex</c>。</summary>
    private readonly object _pendingMu = new();

    /// <summary>Go: <c>terminal error</c>（读循环退出后所有等待中的调用都被它唤醒）。</summary>
    private Exception? _terminal;

    /// <summary>Go: <c>hasCause bool</c>（terminal 是否已经并入过一次原因）。</summary>
    private bool _hasCause;

    /// <summary>Go: <c>writeMu sync.Mutex</c>。</summary>
    private readonly object _writeMu = new();

    /// <summary>Go: <c>handlers sync.WaitGroup</c>（Run 退出前等所有处理中的请求/通知结束）。</summary>
    private readonly WaitGroup _handlers = new();

    private AsyncConn(Stream? rwc, Protocol protocol, Handler handler)
    {
        _rwc = rwc;
        _protocol = protocol;
        _handler = handler;
    }

    /// <summary>Go: <c>func NewAsyncConn(rwc, handler) *AsyncConn</c>（默认用 JSONRPCProtocol）。</summary>
    public static AsyncConn NewAsyncConn(Stream rwc, Handler handler) =>
        NewAsyncConnWithProtocol(rwc, JSONRPCProtocol.NewJSONRPCProtocol(rwc), handler);

    /// <summary>Go: <c>func NewAsyncConnWithProtocol(rwc, protocol, handler) *AsyncConn</c>。</summary>
    public static AsyncConn NewAsyncConnWithProtocol(Stream? rwc, Protocol protocol, Handler handler) =>
        new(rwc, protocol, handler);

    /// <summary>Go: <c>func (c *AsyncConn) SetCollectTiming(enabled bool)</c>。</summary>
    public void SetCollectTiming(bool enabled)
    {
        _timing = enabled ? new TimingCollector() : null;
    }

    /// <summary>Go: <c>func (c *AsyncConn) Run(ctx context.Context) (err error)</c>。</summary>
    public void Run(Context ctx)
    {
        using var handlerCancel = CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken);
        // Go: `handlerCtx, cancelHandlers := context.WithCancel(ctx)`（保留 ctx 上的所有值，见文件头第 8 条）。
        var handlerCtx = new Context(ctx.RequestID, ctx.CheckerLifetime, handlerCancel.Token, ctx.Locale);
        var requestErrors = new RequestErrorSlot();

        Exception? err = null;
        while (true)
        {
            if (ctx.CancellationToken.IsCancellationRequested)
            {
                err = new OperationCanceledException(ctx.CancellationToken);
                break;
            }

            Message? msg;
            try
            {
                msg = _protocol.ReadMessage();
            }
            catch (Exception readErr)
            {
                err = readErr;
                break;
            }

            // Go: `errors.Is(err, io.EOF)` → return nil（Protocol 把 EOF 翻译成 null）。
            if (msg is null)
            {
                break;
            }

            if (msg.IsResponse())
            {
                HandleResponse(msg);
            }
            else if (msg.IsRequest())
            {
                var request = msg;
                _handlers.Add(1);
                Task.Run(() =>
                {
                    try
                    {
                        var requestErr = HandleRequest(handlerCtx, request);
                        if (requestErr is not null && RecordRequestError(requestErr, requestErrors))
                        {
                            // Go: 请求处理失败是终结性的——由读循环这一侧关掉传输，唤醒所有等待者。
                            if (_rwc is not null)
                            {
                                try
                                {
                                    _rwc.Close();
                                }
                                catch (Exception)
                                {
                                    // Go: `_ = c.rwc.Close()`（关闭错误被丢弃）。
                                }
                            }
                        }
                    }
                    finally
                    {
                        _handlers.Done();
                    }
                });
            }
            else if (msg.IsNotification())
            {
                var notification = msg;
                _handlers.Add(1);
                Task.Run(() =>
                {
                    try
                    {
                        HandleNotification(handlerCtx, notification);
                    }
                    finally
                    {
                        _handlers.Done();
                    }
                });
            }
        }

        // Go 的 defer：唤醒等待者 → 取消 handler → 等 handler 结束 → 把请求错误并进返回值。
        ClosePendingCalls(err);
        handlerCancel.Cancel();
        _handlers.Wait();

        if (requestErrors._error is not null)
        {
            err = JoinErrors(err, requestErrors._error);
        }

        if (err is not null)
        {
            ExceptionDispatchInfo.Capture(err).Throw();
        }
    }

    /// <summary>Go: <c>func (c *AsyncConn) closePendingCalls(runErr error)</c>。</summary>
    private void ClosePendingCalls(Exception? runErr)
    {
        lock (_pendingMu)
        {
            RecordTerminalErrorLocked(runErr);
            ClosePendingCallsLocked();
        }
    }

    /// <summary>Go: <c>func (c *AsyncConn) recordRequestError(requestErr error, requestErrors chan&lt;- error) bool</c>。</summary>
    private bool RecordRequestError(Exception requestErr, RequestErrorSlot requestErrors)
    {
        lock (_pendingMu)
        {
            if (!RecordTerminalErrorLocked(requestErr))
            {
                return false;
            }

            requestErrors._error = requestErr;
            ClosePendingCallsLocked();
            return true;
        }
    }

    /// <summary>Go: <c>func (c *AsyncConn) recordTerminalErrorLocked(terminalErr error) bool</c>。</summary>
    private bool RecordTerminalErrorLocked(Exception? terminalErr)
    {
        if (_terminal is null)
        {
            _terminal = new ConnClosedException();
            if (terminalErr is not null)
            {
                _terminal = JoinedTerminalError(terminalErr);
                _hasCause = true;
                return true;
            }
        }
        else if (!_hasCause && terminalErr is not null)
        {
            _terminal = JoinedTerminalError(terminalErr);
            _hasCause = true;
            return true;
        }

        return false;
    }

    /// <summary>Go: <c>errors.Join(ErrConnClosed, cause)</c>。</summary>
    private static ConnClosedException JoinedTerminalError(Exception cause) =>
        new($"ipc: connection closed\n{cause.Message}", cause);

    /// <summary>Go: <c>func (c *AsyncConn) closePendingCallsLocked()</c>（close(ch) + delete）。</summary>
    private void ClosePendingCallsLocked()
    {
        foreach (var pending in _pending.Values)
        {
            pending.Close();
        }

        _pending.Clear();
    }

    /// <summary>Go: <c>func (c *AsyncConn) handleResponse(msg *Message)</c>。</summary>
    private void HandleResponse(Message msg)
    {
        PendingCall? pending = null;
        lock (_pendingMu)
        {
            // Go 在这里直接 `*msg.ID`（响应消息必然带 id，见 Message.Kind）。
            if (msg.ID is not null && _pending.Remove(msg.ID, out var found))
            {
                pending = found;
            }
        }

        pending?.Complete(msg);
    }

    /// <summary>Go: <c>func (c *AsyncConn) handleRequest(ctx, msg) (retErr error)</c>。返回 null 表示已正常答复。</summary>
    private Exception? HandleRequest(Context ctx, Message msg)
    {
        // 元请求在派发给 handler 之前拦截，直接答复，且它们自己不计入耗时统计。
        switch (msg.Method)
        {
            case Ipc.MethodGetServerTiming:
            {
                Exception? timingWriteErr = null;
                lock (_writeMu)
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

                return timingWriteErr is null
                    ? null
                    : new IpcException($"ipc: failed to write server timing response: {timingWriteErr.Message}", timingWriteErr);
            }

            case Ipc.MethodResetServerTiming:
            {
                _timing?.Reset();

                Exception? resetWriteErr = null;
                lock (_writeMu)
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

                return resetWriteErr is null
                    ? null
                    : new IpcException($"ipc: failed to write reset server timing response: {resetWriteErr.Message}", resetWriteErr);
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
            return HandlePanic(msg, panic);
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
        lock (_writeMu)
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

        return writeErr is null
            ? null
            : new IpcException($"ipc: failed to write response: {writeErr.Message}", writeErr);
    }

    /// <summary>Go 的 recover 分支：写 panic 错误响应；写失败时错误里带上原始 panic 文本。</summary>
    private Exception? HandlePanic(Message msg, DebugFailureException panic)
    {
        var panicError = new IpcException($"panic: {panic.Message}\n{panic}");

        Exception? writeErr = null;
        lock (_writeMu)
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

        return writeErr is null
            ? null
            : new IpcException(
                $"ipc: failed to write panic error response: {writeErr.Message} (original panic: {panic.Message})",
                writeErr);
    }

    /// <summary>Go: <c>func (c *AsyncConn) handleNotification(ctx, msg)</c>（Go 侧忽略 handler 的错误）。</summary>
    private void HandleNotification(Context ctx, Message msg)
    {
        // Go: `_ = c.handler.HandleNotification(...)`——返回值被丢弃；通知是单向的，没有错误响应可发。
        // C# 里 handler 用抛异常表达错误，这里同样丢弃（否则任务边界上的未观测异常会打断 Run）。
        try
        {
            _handler.HandleNotification(ctx, msg.Method, msg.Params);
        }
        catch (Exception)
        {
            // 与 Go 的 `_ =` 一致：通知处理失败不影响连接。
        }
    }

    /// <summary>Go: <c>func (c *AsyncConn) Call(ctx, method string, params any) (json.Value, error)</c>。</summary>
    public byte[]? Call(Context ctx, string method, object? @params)
    {
        // 生成唯一请求 id。
        var id = ID.NewIDString($"api{Interlocked.Increment(ref _seq)}");

        // 先登记响应通道再发请求，避免竞态。
        var pending = new PendingCall();
        lock (_pendingMu)
        {
            if (_terminal is not null)
            {
                Rethrow(_terminal);
            }

            _pending[id] = pending;
        }

        try
        {
            lock (_writeMu)
            {
                _protocol.WriteRequest(id, method, @params);
            }

            var resp = pending.Wait(ctx.CancellationToken);
            if (resp is null)
            {
                // Go: `resp, ok := <-responseChan; if !ok { return c.terminal }`
                // （terminal 此时必非 null：closePendingCallsLocked 只在写入 terminal 之后被调用。）
                lock (_pendingMu)
                {
                    Rethrow(_terminal ?? new ConnClosedException());
                }
            }

            if (resp.Error is not null)
            {
                throw new IpcException($"ipc: remote error [{resp.Error.Code}]: {resp.Error.Message}");
            }

            return resp.Result;
        }
        finally
        {
            lock (_pendingMu)
            {
                if (_pending.Remove(id))
                {
                    pending.Close();
                }
            }
        }
    }

    /// <summary>Go: <c>func (c *AsyncConn) Notify(ctx, method string, params any) error</c>。</summary>
    public void Notify(Context ctx, string method, object? @params)
    {
        lock (_pendingMu)
        {
            if (_terminal is not null)
            {
                Rethrow(_terminal);
            }
        }

        lock (_writeMu)
        {
            _protocol.WriteNotification(method, @params);
        }
    }

    /// <summary>Go 的 <c>errors.Join(a, b)</c>：文本按 "\n" 连接、相同实例去重；C# 侧 b 收进 InnerException。</summary>
    private static Exception? JoinErrors(Exception? a, Exception? b) => (a, b) switch
    {
        (null, null) => null,
        (null, { } onlyB) => onlyB,
        ({ } onlyA, null) => onlyA,
        ({ } first, { } second) => ReferenceEquals(first, second)
            ? first
            : new IpcException($"{first.Message}\n{second.Message}", second),
    };

    /// <summary>重新抛出已有异常实例（保留原始栈；Go 里就是把同一个 error 值返回给调用方）。</summary>
    [DoesNotReturn]
    private static void Rethrow(Exception error) => ExceptionDispatchInfo.Capture(error).Throw();

    /// <summary>
    /// Go 的 <c>chan *Message</c>（容量 1）+ <c>close(ch)</c>：
    /// 收到响应 = Complete；连接终止 = Close（结果是 null，对应 Go 里通道被 close 后读到的 ok == false）。
    /// </summary>
    private sealed class PendingCall
    {
        private readonly TaskCompletionSource<Message?> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal void Complete(Message response) => _tcs.TrySetResult(response);

        internal void Close() => _tcs.TrySetResult(null);

        /// <summary>Go 的 <c>select { case &lt;-ctx.Done(): …; case resp, ok := &lt;-ch: … }</c>。</summary>
        internal Message? Wait(CancellationToken cancellationToken)
        {
            _tcs.Task.Wait(Timeout.Infinite, cancellationToken);
            return _tcs.Task.Result;
        }
    }

    /// <summary>Go 的 <c>requestErrors := make(chan error, 1)</c>（只会被写入一次，见 recordRequestError）。</summary>
    private sealed class RequestErrorSlot
    {
        internal Exception? _error;
    }
}
