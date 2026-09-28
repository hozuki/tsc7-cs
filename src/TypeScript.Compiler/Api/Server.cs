// ported from: internal/api/server.go:1-139（整文件）
//
// 与 Go 的偏离（逐条说明）：
//
// 1) `io.ReadCloser`/`io.WriteCloser`/`io.Writer` → C# 的 `Stream`/`Stream`/`TextWriter`（本仓既定映射：
//    `tsc.System.Writer() io.Writer` → `TextWriter`，见 Execute/Tsc/Compile.cs:41；ContentMapper.Spawner.Spawn 的
//    `stderr io.Writer` → `Stream`，见 ContentMapper/HostImpl.cs）。`In`/`Out` 必须可读/可写，默认 `Stream.Null`
//    只是"未设置"的占位（Go 里是 nil 接口）。
//
// 2) `Cwd == ""` 时 Go `panic("StdioServerOptions.Cwd is required")` → C# 抛 `DebugFailureException`（§3.2 panic 映射）。
//
// 3) `(T, error)` → 抛异常（`ErrInvalidRequest` 之外的包装错误用 `ApiServerException` 保留 Go 的 `%w` 文本 +
//    InnerException）。`Run(ctx)` 的返回值在 Go 里是 error；C# 里"正常结束"= 正常返回、"出错"= 抛异常。
//    注意 Go 的 `serverRunError`：**ctx 已取消时把错误吞掉返回 nil**（取消是正常的关闭路径），C# 同义。
//
// 4) `defer t.Close()` / `defer session.Close()` → `try/finally`。`defer` 的顺序是**逆序**，C# 里手写同样的顺序
//    （Go 的 `defer t.Close()` 先注册、后执行；`session.Close()` 后注册、先执行）。为可读性这里把 transport 的
//    关闭放在 finally 的末尾（等价于 Go 的执行顺序）。
//
// 5) `session := NewStandaloneSession(sessionInit, &SessionOptions{UseBinaryResponses: !s.options.Async})`
//    → `Api.Session.NewStandaloneSession(sessionInit, new SessionOptions { UseBinaryResponses = !options.Async })`
//    （`SessionOptions` 是 `TypeScript.Api` 的，与 `TypeScript.Project.SessionOptions` 同名 → 本文件用
//    `ApiSessionOptions` 别名避免歧义，见 `using` 块）。
//
// 6) `var conn ipc.Conn` 的两种实现（AsyncConn/SyncConn）在 C# 里接口相同（`Ipc.Conn`），直接按分支赋给同一个变量。
//
// 7) Go 的 `ipc.NewStdioTransport(s.options.In, s.options.Out)` 收两个 `io.ReadCloser`（Go 的 readCloser 包装）：
//    C# 的 `StdioTransport.NewStdioTransport(Stream stdin, Stream stdout)`（Transport.cs:213）同名同义。
using TypeScript.ContentMapper;
using TypeScript.Core;
using TypeScript.Execute;
using TypeScript.Ipc;
using TypeScript.LsProto;
using TypeScript.Project;
using TypeScript.Vfs.OsVfs;
using ApiSessionOptions = TypeScript.Api.SessionOptions;
using Bundled = TypeScript.Bundled.Bundled;
using ProjectSessionInit = TypeScript.Project.SessionInit;
using ProjectSessionOptions = TypeScript.Project.SessionOptions;

namespace TypeScript.Api;

/// <summary>Go 的 `fmt.Errorf("failed to create pipe transport: %w", err)` / `("failed to accept connection: %w", err)`
/// 等包装错误（保留 Go 的文本，原始异常在 <see cref="Exception.InnerException"/>）。</summary>
public sealed class ApiServerException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>Go: <c>type StdioServerOptions struct</c>（server.go:14-41）：STDIO API 服务器的配置。</summary>
public sealed class StdioServerOptions
{
    /// <summary>Go: <c>In io.ReadCloser</c>。</summary>
    public Stream In = Stream.Null;

    /// <summary>Go: <c>Out io.WriteCloser</c>。</summary>
    public Stream Out = Stream.Null;

    /// <summary>Go: <c>Err io.Writer</c>（C# 侧 `io.Writer` → `TextWriter`，见文件头第 1 条）。</summary>
    public TextWriter? Err;

    /// <summary>Go: <c>Cwd string</c>。</summary>
    public string Cwd = "";

    /// <summary>Go: <c>DefaultLibraryPath string</c>。</summary>
    public string DefaultLibraryPath = "";

    /// <summary>Go: <c>PipePath string</c>：非空时用命名管道（Windows）/ Unix domain socket 通信，忽略 In/Out。</summary>
    public string PipePath = "";

    /// <summary>Go: <c>Callbacks []string</c>：哪些文件系统操作要委派给客户端（如 "readFile"、"fileExists"）。</summary>
    public string[]? Callbacks;

    /// <summary>Go: <c>Async bool</c>：true 用 JSON-RPC（异步连接），false（默认）用 MessagePack（同步连接）。</summary>
    public bool Async;

    /// <summary>Go: <c>CollectTiming bool</c>：逐请求统计服务端处理耗时。</summary>
    public bool CollectTiming;

    /// <summary>Go: <c>RunExternalCode bool</c>：允许配置好的内容映射器执行。</summary>
    public bool RunExternalCode;

    /// <summary>Go: <c>ContentMapperSpawner contentmapper.Spawner</c>。</summary>
    public ContentMapper.Spawner? ContentMapperSpawner;
}

/// <summary>Go: <c>type StdioServer struct</c>（server.go:43-48）：以 MessagePack 协议在 STDIO 上跑一个 API 会话。</summary>
public sealed class StdioServer
{
    /// <summary>Go: <c>options *StdioServerOptions</c>。</summary>
    private readonly StdioServerOptions _options;

    private StdioServer(StdioServerOptions options)
    {
        _options = options;
    }

    /// <summary>Go: <c>func NewStdioServer(options *StdioServerOptions) *StdioServer</c>（server.go:50-58）。
    /// Cwd 为空时 Go panic → C# 抛 <see cref="DebugFailureException"/>（文件头第 2 条）。</summary>
    public static StdioServer NewStdioServer(StdioServerOptions options)
    {
        if (options.Cwd.Length == 0)
        {
            Debug.Fail("StdioServerOptions.Cwd is required");
        }

        return new StdioServer(options);
    }

    /// <summary>Go: <c>func (s *StdioServer) Run(ctx context.Context) error</c>（server.go:60-126）：
    /// 启动服务器并阻塞到连接关闭。</summary>
    public void Run(Context ctx)
    {
        Ipc.Transport transport;
        if (_options.PipePath.Length != 0)
        {
            try
            {
                transport = PipeTransport.NewPipeTransport(_options.PipePath);
            }
            catch (Exception err)
            {
                throw new ApiServerException($"failed to create pipe transport: {err.Message}", err);
            }
        }
        else
        {
            transport = StdioTransport.NewStdioTransport(_options.In, _options.Out);
        }

        try
        {
            var fs = TypeScript.Bundled.Bundled.WrapFS(Osvfs.FS());

            // Wrap the base FS with callbackFS if callbacks are requested
            CallbackFs? callbackFS = null;
            if (_options.Callbacks is { Length: > 0 } callbacks)
            {
                callbackFS = CallbackFs.NewCallbackFs(fs, callbacks);
                fs = callbackFS;
            }

            var sessionInit = new ProjectSessionInit
            {
                BackgroundCtx = ctx,
                Logger = null, // TODO: Add logging support
                FS = fs,
                Options = new ProjectSessionOptions
                {
                    CurrentDirectory = _options.Cwd,
                    DefaultLibraryPath = _options.DefaultLibraryPath,
                    PositionEncoding = PositionEncodingKind.UTF8,
                    LoggingEnabled = false,
                    RunExternalCode = _options.RunExternalCode,
                },
                Spawner = _options.ContentMapperSpawner,
            };

            var session = Session.NewStandaloneSession(sessionInit, new ApiSessionOptions
            {
                UseBinaryResponses = !_options.Async, // Only msgpack uses binary responses
            });

            try
            {
                // Accept connection from transport
                Stream rwc;
                try
                {
                    rwc = transport.Accept();
                }
                catch (Exception err)
                {
                    throw new ApiServerException($"failed to accept connection: {err.Message}", err);
                }

                // Create protocol and connection based on async mode
                Ipc.Conn conn;
                if (_options.Async)
                {
                    var protocol = JSONRPCProtocol.NewJSONRPCProtocol(rwc);
                    var asyncConn = AsyncConn.NewAsyncConnWithProtocol(rwc, protocol, session);
                    asyncConn.SetCollectTiming(_options.CollectTiming);
                    conn = asyncConn;
                }
                else
                {
                    var protocol = MessagePackProtocol.NewMessagePackProtocol(rwc);
                    var syncConn = SyncConn.NewSyncConn(rwc, protocol, session);
                    syncConn.SetCollectTiming(_options.CollectTiming);
                    conn = syncConn;
                }

                // If callbacks are enabled, set the connection on the FS
                callbackFS?.SetConnection(ctx, conn);
                session.SetConnection(conn);

                RunConnection(ctx, conn);
            }
            finally
            {
                session.Close();
            }
        }
        finally
        {
            transport.Close();
        }
    }

    /// <summary>Go 的 <c>return serverRunError(ctx, conn.Run(ctx))</c>（server.go:125）：连接运行结束后，
    /// **ctx 已取消就吞掉错误**（取消是正常关闭路径）。</summary>
    private static void RunConnection(Context ctx, Ipc.Conn conn)
    {
        try
        {
            conn.Run(ctx);
        }
        catch (Exception err)
        {
            var rethrow = Api.ServerRunError(ctx, err);
            if (rethrow is not null)
            {
                throw rethrow;
            }
        }
    }
}

/// <summary>Go 的 api 包级函数（server.go 的 `serverRunError`）：C# 落成静态类 `Api` 的成员（先例 `Ipc.Ipc`）。</summary>
public static partial class Api
{
    /// <summary>Go: <c>func serverRunError(ctx context.Context, err error) error</c>（server.go:128-133）：
    /// `ctx.Err() != nil` 时返回 nil（把取消当成正常结束），否则原样返回 err。
    /// C# 侧返回**要抛的异常**或 null（`(T, error)` 的异常化口径，见 Conn.cs 文件头第 3 条）。</summary>
    internal static Exception? ServerRunError(Context ctx, Exception err)
    {
        if (ctx.CancellationToken.IsCancellationRequested)
        {
            return null;
        }

        return err;
    }
}
