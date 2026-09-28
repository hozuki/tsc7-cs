// ported from: internal/testutil/lsptestutil/lspclient.go
//
// C# 映射与偏离（逐条）：
// 1) Go 的 `*testing.T` → `IHarnessTest`（见 TestUtil/HarnessContext.cs）：`t.Fatalf` → `Fatal`，
//    `t.Context()` → `CancellationToken`。`t.Helper()` 无对应物，直接丢弃。
// 2) Go 的 `io.Pipe()`（同步内存管道）+ `lsp.ToReader/ToWriter` → 本文件内的 `BlockingPipeStream`
//    （写侧 append 到无界 `BlockingCollection<byte[]>`，读侧阻塞取块；写侧关闭后读侧返回 0 = Go 的 io.EOF）
//    与 `TypeScript.Lsp` 的 `ToReader/ToWriter`。与 Go 一样是**两个方向各一条流**，且客户端/服务端共享同一实例。
// 3) Go 的 errgroup + `ctx` 取消 → 两条 `Thread`（server.Run / MessageRouter）+ `CancellationTokenSource`；
//    Go 的 `closeClient()` 返回 error → C# 的 `Func<Exception?>`（null = 无错），调用方按 `t.Errorf` 上报。
// 4) Go 的 `chan *lsproto.ResponseMessage`（容量 1）→ `BlockingCollection<LsProto.ResponseMessage>(1)`
//    （同样有界为 1，`Add` 语义与 Go 的缓冲通道一致）。
// 5) `json.MarshalWrite(io.Discard, msg)`（只做可编解码性校验）→ `Marshal(msg)`（返回的字节丢弃）。
// 6) Go 的 `lsproto.RequestInfo[Params, Resp]` 泛型方法 → C# 泛型方法（`RequestInfo<Params, Resp>` 是 struct）。
//    Go 失败路径的 `*new(Resp)` → `default(Resp)!`（引用类型为 null，与 Go 的零值指针一致）。
// 7) Go 的 `errors.Is(err, io.EOF)` → `EndOfStreamException`（Lsp 读侧的流结束信号）。
// 8) Go 的 `context.Context` → `CancellationToken`（本仓已有映射）。
using System.Collections.Concurrent;
using TypeScript.Json;
using TypeScript.JsonRpc;
using TypeScript.LsProto;
using TypeScript.TestUtil;
using static TypeScript.Json.Json;
// JsonRpc 与 LsProto 各有一个 RequestMessage/ResponseMessage/Reader/Writer/IntegerOrString
// （后者才是 lspclient.go 用的那一套）→ 变量别名消歧（与 LsProto/JsonRpc.cs 的写法一致）。
using RequestMessage = TypeScript.LsProto.RequestMessage;
using ResponseMessage = TypeScript.LsProto.ResponseMessage;
using Reader = TypeScript.Lsp.Reader;
using Writer = TypeScript.Lsp.Writer;
using IntegerOrString = TypeScript.LsProto.IntegerOrString;
using LsProtoMessage = TypeScript.LsProto.Message;

namespace TypeScript.TestUtil.LspTestUtil;

/// <summary>Go: <c>type ServerRequestHandler func(ctx context.Context, req *lsproto.RequestMessage) *lsproto.ResponseMessage</c>。</summary>
public delegate ResponseMessage? ServerRequestHandler(CancellationToken ctx, RequestMessage req);

/// <summary>Go: <c>type ServerNotificationHandler func(ctx context.Context, req *lsproto.RequestMessage)</c>。</summary>
public delegate void ServerNotificationHandler(CancellationToken ctx, RequestMessage req);

/// <summary>
/// Go: <c>type clientTransport struct</c>。测试客户端与服务端之间走真实的 "Content-Length" 分帧 JSON 字节流
/// （与真实编辑器通信完全一致，覆盖每个协议结构的完整 marshal/unmarshal 往返）。两个方向：
/// <code>
/// client --(clientOut)--> serverIn --> server
/// server --(serverOut)--> clientIn --> client
/// </code>
/// </summary>
public sealed class ClientTransport
{
    /// <summary>服务端读客户端→服务端方向。</summary>
    public Reader ServerIn = null!;

    /// <summary>服务端写服务端→客户端方向。</summary>
    public Writer ServerOut = null!;

    /// <summary>客户端读服务端→客户端方向。</summary>
    public Reader ClientIn = null!;

    /// <summary>客户端写客户端→服务端方向。</summary>
    public Writer ClientOut = null!;

    /// <summary>关闭客户端→服务端方向。</summary>
    public Action CloseClientOut = null!;

    /// <summary>关闭服务端→客户端方向。</summary>
    public Action CloseServerOut = null!;
}

/// <summary>Go: <c>func newClientTransport() clientTransport</c>。</summary>
public static class Transport
{
    /// <summary>Go: <c>func newClientTransport() clientTransport</c>（两个 `io.Pipe()`）。</summary>
    public static ClientTransport NewClientTransport()
    {
        var clientToServer = new BlockingPipeStream();
        var serverToClient = new BlockingPipeStream();
        return new ClientTransport
        {
            ServerIn = TypeScript.Lsp.Lsp.ToReader(clientToServer),
            ServerOut = TypeScript.Lsp.Lsp.ToWriter(serverToClient),
            ClientIn = TypeScript.Lsp.Lsp.ToReader(serverToClient),
            ClientOut = TypeScript.Lsp.Lsp.ToWriter(clientToServer),
            CloseClientOut = clientToServer.CloseWrite,
            CloseServerOut = serverToClient.CloseWrite,
        };
    }
}

/// <summary>
/// Go: <c>io.Pipe()</c> 的等价物（同步内存管道，只保留管道的一端：写入本对象的字节由本对象的读取方读出）。
/// 与 Go 一样是字节流：写入按 chunk 排队，读侧跨 chunk 取；写侧关闭后读侧读到 0（= Go 的 <c>io.EOF</c>）。
/// </summary>
public sealed class BlockingPipeStream : Stream
{
    private readonly BlockingCollection<byte[]> _chunks = [];
    private byte[]? _current;
    private int _offset;

    /// <summary>Go: <c>func (w *io.PipeWriter) Close() error</c>。</summary>
    public void CloseWrite()
    {
        if (!_chunks.IsAddingCompleted)
        {
            _chunks.CompleteAdding();
        }
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        while (true)
        {
            if (_current is not null)
            {
                var n = Math.Min(count, _current.Length - _offset);
                Array.Copy(_current, _offset, buffer, offset, n);
                _offset += n;
                if (_offset == _current.Length)
                {
                    _current = null;
                    _offset = 0;
                }

                return n;
            }

            // 无数据：阻塞等下一块；写侧关闭且队列空 → TryTake 返回 false（= Go 的 io.EOF）。
            if (_chunks.IsCompleted)
            {
                return 0;
            }

            if (!_chunks.TryTake(out var chunk, Timeout.Infinite))
            {
                return 0;
            }

            _current = chunk;
            _offset = 0;
        }
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        var copy = new byte[count];
        Array.Copy(buffer, offset, copy, 0, count);
        // 写侧不阻塞（Go 的 io.Pipe 会阻塞到读侧收下，但这里读侧永远在收，且写入本身是 O(1) 入队；
        // 有界阻塞反而会在读线程尚未启动时死锁）。
        _chunks.Add(copy);
    }

    public override void Write(ReadOnlySpan<byte> buffer) => Write(buffer.ToArray(), 0, buffer.Length);

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();
}

/// <summary>
/// Go: <c>type LSPClient struct</c>。测试中与 LSP 服务器通信的基础设施。
/// </summary>
public sealed class LSPClient
{
    /// <summary>Go 的导出字段 <c>Server *lsp.Server</c>。</summary>
    public TypeScript.Lsp.Server Server = null!;

    private Writer _inputWriter = null!;
    private Reader _outputReader = null!;
    private int _id;
    private CancellationToken _ctx;
    private Core.Context _coreCtx = null!;

    // Go 的 `inputWriterMu sync.Mutex`：测试 goroutine（发请求/通知）与 MessageRouter goroutine
    // （回服务器发起的请求）都会往同一条流写，而一条消息是分多次底层写（头、体、flush）完成的，
    // 所以并发写必须串行化。
    private readonly object _inputWriterMu = new();

    // Go 的 `onServerRequest ServerRequestHandler`（nil = 所有服务器请求收到 MethodNotFound）。
    private ServerRequestHandler? _onServerRequest;

    /// <summary>Go 的 `OnServerNotification ServerNotificationHandler`（nil = 忽略通知）。</summary>
    public ServerNotificationHandler? OnServerNotification;

    // Go 的 `pendingRequests map[jsonrpc.ID]chan *lsproto.ResponseMessage`。
    private readonly Dictionary<ID, BlockingCollection<ResponseMessage>> _pendingRequests = [];
    private readonly object _pendingRequestsMu = new();

    private Thread _serverThread = null!;
    private Thread _routerThread = null!;
    private volatile Exception? _serverError;
    private volatile Exception? _routerError;

    /// <summary>非 Go 源（诊断用）：本客户端的取消令牌（Go 的未导出字段 <c>c.ctx</c>）。用例在等
    /// <c>InitComplete</c> 这类**不可取消**的信号时，用它判断"客户端这一侧是不是已经失败/被取消了"。</summary>
    internal CancellationToken Context => _ctx;

    /// <summary>非 Go 源（诊断用）：泵线程里记录下来的失败（非 null 说明会话已经不可用）。</summary>
    internal Exception? Failure => _routerError ?? _serverError;

    private LSPClient()
    {
    }

    /// <summary>Go: <c>func (c *LSPClient) writeToServer(msg *lsproto.Message) error</c>（串行化并发写）。</summary>
    private void WriteToServer(LsProtoMessage msg)
    {
        lock (_inputWriterMu)
        {
            _inputWriter.Write(msg);
        }
    }

    /// <summary>Go: <c>func NewLSPClient(t, serverOpts, onServerRequest) (*LSPClient, func() error)</c>。</summary>
    public static (LSPClient Client, Func<Exception?> Close) NewLSPClient(IHarnessTest t, TypeScript.Lsp.ServerOptions serverOptions, ServerRequestHandler? onServerRequest)
    {
        var transport = Transport.NewClientTransport();
        serverOptions.In = transport.ServerIn;
        serverOptions.Out = transport.ServerOut;

        var server = new TypeScript.Lsp.Server(serverOptions);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(t.CancellationToken);
        var ctx = cts.Token;
        var coreCtx = TypeScript.Core.Core.WithCancellation(TypeScript.Core.Context.Background, ctx);
        var client = new LSPClient
        {
            Server = server,
            _inputWriter = transport.ClientOut,
            _outputReader = transport.ClientIn,
            _onServerRequest = onServerRequest,
            _ctx = ctx,
            _coreCtx = coreCtx,
        };

        // Go: g.Go(func() error { defer transport.closeServerOut(); return server.Run(ctx) })
        client._serverThread = new Thread(() =>
        {
            try
            {
                server.Run(coreCtx);
            }
            catch (Exception e)
            {
                client._serverError = e;
                TypeScript.TestRunner.HarnessProgress.Note($"client server thread caught {e.GetType().Name}: {e.Message}");

                // Go 的 errgroup.WithContext：任一 goroutine 返回非 nil error 会取消派生的 ctx，
                // 于是等待响应的 waitForResponse 立刻走 ctx.Done() 分支（t.Fatalf）而不是永远挂住。
                cts.Cancel();
            }
            finally
            {
                transport.CloseServerOut();
            }
        })
        { IsBackground = true, Name = "lsp-server" };

        // Go: g.Go(func() error { return client.MessageRouter(ctx) })
        client._routerThread = new Thread(() =>
        {
            try
            {
                client.MessageRouter(ctx);
            }
            catch (Exception e)
            {
                client._routerError = e;
                cts.Cancel();
            }
        })
        { IsBackground = true, Name = "lsp-client-router" };

        client._serverThread.Start();
        client._routerThread.Start();

        // 非 Go 源：登记给 `--case-timeout` 的看门狗（超时时 dump 这两条泵线程的栈，见 HarnessProgress）。
        TypeScript.TestRunner.HarnessProgress.RegisterThread(client._serverThread);
        TypeScript.TestRunner.HarnessProgress.RegisterThread(client._routerThread);

        return (client, () =>
        {
            TypeScript.TestRunner.HarnessProgress.Note("closeClient: cancelling the client context and closing the input stream");
            cts.Cancel();
            transport.CloseClientOut();
            // 非 Go 源：超时诊断（收尾等待：泵线程退出）。
            using var waiting = TypeScript.TestRunner.HarnessProgress.Wait("waiting for the client router/server threads to exit");
            if (!client._routerThread.Join(TimeSpan.FromSeconds(30)))
            {
                return new TimeoutException("message router did not exit");
            }

            if (!client._serverThread.Join(TimeSpan.FromSeconds(30)))
            {
                return new TimeoutException("server did not exit");
            }

            if (client._routerError is not null)
            {
                return client._routerError;
            }

            if (client._serverError is { } serverError && !IsContextCanceled(serverError, ctx))
            {
                return serverError;
            }

            TypeScript.TestRunner.HarnessProgress.Note("closeClient: done");
            return null;
        });
    }

    /// <summary>Go 的 <c>errors.Is(err, context.Canceled)</c>（C# 的取消是 OCE）。</summary>
    private static bool IsContextCanceled(Exception e, CancellationToken ctx) =>
        ctx.IsCancellationRequested && e is OperationCanceledException;

    /// <summary>Go: <c>func (c *LSPClient) NextID() int32</c>。</summary>
    public int NextID()
    {
        var id = _id;
        _id++;
        return id;
    }

    /// <summary>
    /// Go: <c>func (c *LSPClient) MessageRouter(ctx context.Context) error</c>。路由来自服务端的消息：
    /// 客户端请求的响应、服务端发起的请求。即使 ctx 取消也要继续把输出流读干（EOF）为止，
    /// 否则服务端的 writeLoop 会阻塞在发送上。
    /// </summary>
    public void MessageRouter(CancellationToken ctx)
    {
        while (true)
        {
            LsProtoMessage msg;
            // 非 Go 源：超时诊断（这条泵线程是"server → client"两个方向的中枢）。
            using var reading = TypeScript.TestRunner.HarnessProgress.Wait("client router reading a message from the server");
            try
            {
                msg = _outputReader.Read();
            }
            catch (Exception e)
            {
                if (e is EndOfStreamException || ctx.IsCancellationRequested)
                {
                    return;
                }

                throw new Exception($"failed to read message: {e.Message}", e);
            }

            // ctx 取消后继续排空，但不再处理消息。
            if (ctx.IsCancellationRequested)
            {
                continue;
            }

            // 校验消息可被编码。
            try
            {
                Marshal(msg);
            }
            catch (Exception e)
            {
                if (ctx.IsCancellationRequested)
                {
                    continue;
                }

                throw new Exception($"failed to encode message as JSON: {e.Message}", e);
            }

            switch (msg.Kind)
            {
                case MessageKind.Response:
                    HandleResponse(ctx, msg.AsResponse());
                    break;
                case MessageKind.Request:
                    HandleServerRequest(ctx, msg.AsRequest());
                    break;
                case MessageKind.Notification:
                    OnServerNotification?.Invoke(ctx, msg.AsRequest());
                    break;
            }
        }
    }

    /// <summary>Go: <c>func (c *LSPClient) handleResponse(ctx, resp)</c>（把响应路由到等待中的请求 goroutine）。</summary>
    private void HandleResponse(CancellationToken ctx, ResponseMessage resp)
    {
        if (resp.ID is null)
        {
            return;
        }

        BlockingCollection<ResponseMessage>? respChan;
        lock (_pendingRequestsMu)
        {
            if (_pendingRequests.TryGetValue(resp.ID, out respChan))
            {
                _pendingRequests.Remove(resp.ID);
            }
        }

        if (respChan is not null)
        {
            try
            {
                respChan.Add(resp, ctx);
            }
            catch (OperationCanceledException)
            {
                // context cancelled
            }
            catch (InvalidOperationException)
            {
                // 通道已完成（Go 的 select 到 ctx.Done() 分支）。
            }
        }
    }

    /// <summary>Go: <c>func (c *LSPClient) handleServerRequest(ctx, req) error</c>（如 workspace/configuration）。</summary>
    private void HandleServerRequest(CancellationToken ctx, RequestMessage req)
    {
        ResponseMessage? response = null;

        if (_onServerRequest is not null)
        {
            response = _onServerRequest(ctx, req);
        }

        if (response is null)
        {
            // 默认：未知的服务器请求。
            response = new ResponseMessage
            {
                ID = req.ID,
                JSONRPC = req.JSONRPC,
                Error = new ResponseError
                {
                    Code = (int)ErrorCode.MethodNotFound,
                    Message = $"Unknown method: {req.Method.Value}",
                },
            };
        }

        // 把响应回发给服务端。
        if (ctx.IsCancellationRequested)
        {
            return;
        }

        try
        {
            WriteToServer(response.Message());
        }
        catch (Exception e)
        {
            if (ctx.IsCancellationRequested)
            {
                return;
            }

            throw new Exception($"failed to write server request response: {e.Message}", e);
        }
    }

    /// <summary>Go: <c>func (c *LSPClient) WriteMsg(t, msg)</c>（未类型化的底层方法；优先用 SendRequest/SendNotification）。</summary>
    public void WriteMsg(IHarnessTest t, LsProtoMessage msg)
    {
        try
        {
            Marshal(msg);
        }
        catch (Exception e)
        {
            t.Fatal($"failed to encode message as JSON: {e.Message}");
            return;
        }

        try
        {
            WriteToServer(msg);
        }
        catch (Exception e)
        {
            t.Fatal($"failed to write message: {e.Message}");
        }
    }

    /// <summary>Go: <c>func (c *LSPClient) SendRequest[Params, Resp](t, info, params) (*lsproto.Message, Resp, bool)</c>。</summary>
    public (LsProtoMessage? Message, Resp Result, bool Ok) SendRequest<Params, Resp>(IHarnessTest t, RequestInfo<Params, Resp> info, Params parameters)
    {
        var id = NextID();
        var reqID = LsProto.LsProto.NewID(new IntegerOrString { Integer = id });
        var req = info.NewRequestMessage(reqID, parameters);

        var (resp, ok) = SendRequestWorker(t, req, reqID);
        if (!ok)
        {
            return (null, default(Resp)!, false);
        }

        // 结果以原始 json.Value 到达；解码成 Resp。
        try
        {
            return (resp!.Message(), info.UnmarshalResult(resp.Result), true);
        }
        catch (Exception)
        {
            return (resp!.Message(), default(Resp)!, false);
        }
    }

    /// <summary>Go: <c>func (c *LSPClient) SendRequestAsync[Params, Resp](t, info, params) func() (*lsproto.Message, Resp, bool)</c>。</summary>
    public Func<(LsProtoMessage? Message, Resp Result, bool Ok)> SendRequestAsync<Params, Resp>(IHarnessTest t, RequestInfo<Params, Resp> info, Params parameters)
    {
        var id = NextID();
        var reqID = LsProto.LsProto.NewID(new IntegerOrString { Integer = id });
        var req = info.NewRequestMessage(reqID, parameters);

        var responseChan = StartRequestWorker(t, req, reqID);
        return () =>
        {
            var (resp, ok) = WaitForResponse(t, reqID, responseChan);
            if (!ok)
            {
                return (null, default(Resp)!, false);
            }

            try
            {
                return (resp!.Message(), info.UnmarshalResult(resp.Result), true);
            }
            catch (Exception)
            {
                return (resp!.Message(), default(Resp)!, false);
            }
        };
    }

    /// <summary>Go: <c>func (c *LSPClient) SendRequestWorker(t, req, reqID) (*lsproto.ResponseMessage, bool)</c>。</summary>
    public (ResponseMessage? Response, bool Ok) SendRequestWorker(IHarnessTest t, RequestMessage req, ID reqID)
    {
        var responseChan = StartRequestWorker(t, req, reqID);
        return WaitForResponse(t, reqID, responseChan);
    }

    private BlockingCollection<ResponseMessage> StartRequestWorker(IHarnessTest t, RequestMessage req, ID reqID)
    {
        var responseChan = new BlockingCollection<ResponseMessage>(1);
        lock (_pendingRequestsMu)
        {
            _pendingRequests[reqID] = responseChan;
        }

        WriteMsg(t, req.Message());
        return responseChan;
    }

    private (ResponseMessage? Response, bool Ok) WaitForResponse(IHarnessTest t, ID reqID, BlockingCollection<ResponseMessage> responseChan)
    {
        var ctx = _ctx;
        ResponseMessage? resp;
        // 非 Go 源：超时诊断（把"在等哪条请求的应答"登记到 HarnessProgress，超时时打印）。
        using var wait = TypeScript.TestRunner.HarnessProgress.Wait($"client waiting for response to request {reqID}");
        try
        {
            if (!responseChan.TryTake(out resp, Timeout.Infinite, ctx))
            {
                return (null, false);
            }
        }
        catch (OperationCanceledException e)
        {
            lock (_pendingRequestsMu)
            {
                _pendingRequests.Remove(reqID);
            }

            t.Fatal($"Request cancelled: {e.Message}");
            return (null, false);
        }

        if (resp is null)
        {
            return (null, false);
        }

        return (resp, true);
    }

    /// <summary>Go: <c>func (c *LSPClient) SendNotification[Params](t, info, params)</c>。</summary>
    public void SendNotification<Params>(IHarnessTest t, NotificationInfo<Params> info, Params parameters)
    {
        var notification = info.NewNotificationMessage(parameters);
        WriteMsg(t, notification.Message());
    }

    /// <summary>Go: <c>func (c *LSPClient) SetCompilerOptionsForInferredProjects(options *core.CompilerOptions)</c>。</summary>
    public void SetCompilerOptionsForInferredProjects(TypeScript.Core.CompilerOptions options)
    {
        Server.SetCompilerOptionsForInferredProjects(_coreCtx, options);
    }
}
