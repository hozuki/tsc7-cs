// ported from: internal/lsp/server.go:1-2539（整文件；本切片按 Go 文件内的功能段落拆成多个 partial 文件：
//   Server.cs（本文件）= Reader/Writer 传输层 + Server 结构 + Run/readLoop/dispatchLoop/writeLoop + send/recover
//   Server.Client.cs   = project.Client / ata.NpmExecutor 的实现 + 内容映射器注册 + 配置请求
//   Server.Initialize.cs = initialize/initialized/shutdown/exit/did* + willRenameFiles
//   Server.Handlers.cs  = handler 注册表 + crossProjectOrchestrator
//   Server.Requests.cs  = 语言服务请求转发
//   Server.Dev.cs       = 开发者/调试命令 + 内容映射器贡献解析
// 全部内容与 Go 的 server.go 逐段对应，仅做了文件切分。）
//
// 与 Go 的偏离（逐条说明）：
//
// 1) **每个请求一个 64 MiB 栈线程**（本切片最关键的偏离）。Go 的 dispatchLoop 在 goroutine 里跑 handler 的
//    "同步段"，再把返回的异步段放到 `go func()` 里；Go 的 goroutine 栈可增长，而 .NET 线程栈固定，
//    `StackOverflowException` **不可捕获**（docs/architecture.md:168-174）。因此：
//      * 同步段仍在 dispatch 线程上按序执行（与 Go 的"顺序屏障"一致：didOpen 之类通知的同步段就是全部工作）；
//      * 异步段（真正会深递归的 bind/check/print）派发到一个**显式 64 MiB 栈**的专用线程上，而不是线程池
//        （先例：Execute/Build/Orchestrator.cs、Project/Background/Queue.cs、tests/TypeScript.Harness）。
//    这一条是"必须这样、否则深递归请求会不可捕获地崩"的硬要求。
//
// 2) **Reader/Writer 的 EOF 语义**：Go 的 `Read() (*lsproto.Message, error)` 用 `io.EOF` 表示读到流尾；
//    C# 侧 `LsProto.BaseReader.Read()`（jsonrpc.Reader.Read）读到流尾抛 `EndOfStreamException`
//    （Ipc/ConnSync.cs:283 的既定约定），因此 `Reader.Read()` 同样抛它。
//
// 3) **解码失败要带回部分消息**：Go 的 `json.Unmarshal(data, req)` 失败时仍把 `req` 返回给 readLoop，
//    readLoop 据此在 InvalidParams 时取出请求 ID 再回错误响应。C# 的错误路径是异常，而
//    `ErrorCodeException` 是 sealed 的，所以新增内部异常 `MessageReadException`（带 `Code` 与 `PartialMessage`）。
//    注：C# 的 `LsProto.Message.UnmarshalJSONFrom` 只在**全部字段读完后**才写回 `Kind`/`_msg`，所以
//    `PartialMessage.Kind` 通常是零值（Go 侧同样只在成功时才填好 msg），readLoop 因此可能拿不到请求 ID
//    —— 这是这条路径（客户端发来畸形 params）的唯一可观察差异。
//
// 4) **错误一律是异常**（docs §3.2）。Go 的 `(T, error)` 在这里分成两种写法：
//      * 需要区分"取消/EOF/真错误"的地方（readLoop）→ 捕获 `OperationCanceledException`/`EndOfStreamException`；
//      * 只用来报告"发送失败"的 `send*` 系列 → 直接抛，调用点用 try/catch。
//    `run`/`g.Wait()` 的 errgroup 语义（启动 dispatch+write，readLoop 单独跑，读到 EOF/错误即取消其余循环）
//    在 C# 侧内联进 `Run`：不是 `Task`/`Parallel`，而是三条显式线程（见第 1 条）。
//
// 5) `context.WithCancelCause(ctx)` + `lspExit(err)` → `CancellationTokenSource`（.NET 没有 cause，
//    退出原因存在 `_dispatchExitError` 里供 `Run` 上报；Go 的 Run 在绝大多数情况下本来就返回 nil）。
//
// 6) `pendingServerRequests map[jsonrpc.ID]chan *lsproto.ResponseMessage`（容量 1 的 channel，用 send/close
//    表达"有响应"）→ `Dictionary<JsonRpcID, BlockingCollection<LsProto.ResponseMessage>>`：
//    `respChan <- resp` = `Add`，`close(respChan)` = `CompleteAdding()`，
//    `select { case <-ctx.Done(): case resp := <-respChan: }` = `TryTake(out resp, Timeout.Infinite, ctx.Token)`。
//
// 7) `pprof` 的 CPU profiler 是 `TypeScript.Execute.Pprof.CPUProfiler`（no-op 会话，见 Execute/Pprof.cs 文件头）；
//    调用点照 Go 的形状写（返回值 → `bool TryX(...)`）。
//
// 8) `(*Server).Session()`（Go 的方法）→ C# 的只读属性 `Session`（Go 的方法体只读一个字段）。
//
// 9) `handleInitializeAPISession` / `generateAPIPipePath` / `removeAPISession` / `apiSessions` **未移植**：
//    Go 用 `internal/api.NewLSPSession`，而 `internal/api`（≈8,226 行）不在本切片（计划 §P7），C# 侧尚不存在。
//    因此 `CustomInitializeAPISessionInfo` 也**不在** handler 表里注册（客户端会收到 method-not-found，
//    与"该功能尚未移植"这一事实一致；注册一个必然失败的空 handler 会更糟）。
//    这一项与 `internal/api` 一起在计划的 P7 补齐；`Server` 侧要改的只有：加 `apiSessions` 字段 + 该 handler。
using System.Collections.Concurrent;
using System.Text;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.LsProto;
using TypeScript.Vfs;
using static TypeScript.Core.Core;
using static TypeScript.LsProto.LsProto;
using Loc = TypeScript.Locale.Locale;
using LspMessage = TypeScript.LsProto.Message;
using MessageKind = TypeScript.JsonRpc.MessageKind;
using JsonRpcID = TypeScript.JsonRpc.ID;
using JsonRpcResponseError = TypeScript.JsonRpc.ResponseError;
using ProjectSession = TypeScript.Project.Session;

namespace TypeScript.Lsp;

/// <summary>Go: <c>type Reader interface</c>（server.go:105-107）：按 LSP 帧读一条消息。读到流尾抛 <see cref="EndOfStreamException"/>。</summary>
public interface Reader
{
    /// <summary>Go: <c>Read() (*lsproto.Message, error)</c>。</summary>
    LspMessage Read();
}

/// <summary>Go: <c>type Writer interface</c>（server.go:109-111）：把一条消息按 LSP 帧写出。</summary>
public interface Writer
{
    /// <summary>Go: <c>Write(msg *lsproto.Message) error</c>。</summary>
    void Write(LspMessage msg);
}

/// <summary>Go: <c>type lspReader struct</c>（server.go:113-115）。</summary>
internal sealed class LspReader(BaseReader reader) : Reader
{
    private readonly BaseReader _r = reader;

    /// <summary>Go: <c>func (r *lspReader) Read() (*lsproto.Message, error)</c>（server.go:134-147）。</summary>
    public LspMessage Read()
    {
        var data = _r.Read();

        var req = new LspMessage();
        try
        {
            Json.Json.UnmarshalInto(data, ref req);
        }
        catch (ErrorCodeException e) when (e.Code == ErrorCode.InvalidParams)
        {
            throw new MessageReadException(ErrorCode.InvalidParams, req,
                $"{ErrorCode.InvalidParams.String()}: {e.Message}", e);
        }
        catch (Exception e)
        {
            throw new MessageReadException(ErrorCode.InvalidRequest, null,
                $"{ErrorCode.InvalidRequest.String()}: {e.Message}", e);
        }

        return req;
    }
}

/// <summary>Go: <c>func ToReader(r io.Reader) Reader</c>（server.go:149-151）。</summary>
/// <summary>Go: <c>func ToWriter(w io.Writer) Writer</c>（server.go:153-155）。</summary>
/// <remarks>Go 把这两个包级函数放在 server.go；C# 侧按"Go 包级函数 → 同名静态类"落在 <c>Lsp</c> 上（见 G1 约定）。</remarks>
public static partial class Lsp
{
    /// <summary>Go: <c>func ToReader(r io.Reader) Reader</c>（server.go:149-151）。</summary>
    public static Reader ToReader(Stream stream) => new LspReader(BaseReader.NewBaseReader(stream));

    /// <summary>Go: <c>func ToWriter(w io.Writer) Writer</c>（server.go:153-155）。</summary>
    public static Writer ToWriter(Stream stream) => new LspWriter(BaseWriter.NewBaseWriter(stream));
}

/// <summary>Go: <c>type lspWriter struct</c>（server.go:117-119）。</summary>
internal sealed class LspWriter(BaseWriter writer) : Writer
{
    private readonly BaseWriter _w = writer;

    /// <summary>Go: <c>func (w *lspWriter) Write(msg *lsproto.Message) error</c>（server.go:157-163）。</summary>
    public void Write(LspMessage msg)
    {
        byte[] data;
        try
        {
            data = Json.Json.Marshal(msg);
        }
        catch (Exception e)
        {
            throw new MessageMarshalError(e);
        }

        _w.Write(data);
    }
}

/// <summary>Go: <c>type messageMarshalError struct</c>（server.go:121-129）。</summary>
internal sealed class MessageMarshalError(Exception err) : Exception("failed to marshal message: " + err.Message, err)
{
    /// <summary>Go: <c>func (e *messageMarshalError) Unwrap() []error</c>：同时是 InternalError 与内层错误。</summary>
    internal readonly ErrorCode Code = ErrorCode.InternalError;

    internal readonly Exception Outer = err;
}

/// <summary>
/// C# 侧新增（见文件头第 3 条）：承载"消息解码失败"的错误码与已部分填充的消息，
/// 让 readLoop 能照 Go 的形状处理 InvalidRequest / InvalidParams 两条分支。
/// </summary>
internal sealed class MessageReadException(ErrorCode code, LspMessage? partialMessage, string message, Exception? inner)
    : Exception(message, inner)
{
    internal readonly ErrorCode Code = code;
    internal readonly LspMessage? PartialMessage = partialMessage;
}

/// <summary>Go: <c>type ServerOptions struct</c>（server.go:36-54）。</summary>
public sealed class ServerOptions
{
    /// <summary>Go: <c>In Reader</c>。</summary>
    public Reader In = null!;

    /// <summary>Go: <c>Out Writer</c>。</summary>
    public Writer Out = null!;

    /// <summary>Go: <c>Err io.Writer</c>（io.Writer → TextWriter，本仓约定）。</summary>
    public TextWriter Err = TextWriter.Null;

    /// <summary>Go: <c>Cwd string</c>（必填；空则 panic → C# 抛 <see cref="InvalidOperationException"/>）。</summary>
    public string Cwd = "";

    /// <summary>Go: <c>FS vfs.FS</c>。</summary>
    public FS FS = null!;

    /// <summary>Go: <c>DefaultLibraryPath string</c>。</summary>
    public string DefaultLibraryPath = "";

    /// <summary>Go: <c>TypingsLocation string</c>。</summary>
    public string TypingsLocation = "";

    /// <summary>Go: <c>ParseCache *project.ParseCache</c>（可传入以便多个测试共享 AST）。</summary>
    public global::TypeScript.Project.ParseCache? ParseCache;

    /// <summary>Go: <c>NpmInstall func(cwd string, args []string) ([]byte, error)</c>。</summary>
    public Func<string, string[], byte[]>? NpmInstall;

    /// <summary>Go: <c>Spawn func(command []string, dir string, stderr io.Writer) (io.ReadWriteCloser, error)</c>。
    /// 宿主不能起子进程时为 nil；目前用于 content mapper。</summary>
    public global::TypeScript.ContentMapper.Spawner? Spawn;

    /// <summary>Go: <c>ProgressDelay time.Duration</c>：显示进度 UI 前的延迟；0 表示不延迟。</summary>
    public TimeSpan ProgressDelay;

    /// <summary>Go: <c>SetParentProcessID func(parentPID int)</c>。</summary>
    public Action<int>? SetParentProcessID;
}

/// <summary>Go: <c>type pendingClientRequest struct</c>（server.go:99-102）。</summary>
internal sealed class PendingClientRequest(RequestMessage req, CancellationTokenSource cancel)
{
    /// <summary>Go: <c>req *lsproto.RequestMessage</c>。</summary>
    internal readonly RequestMessage _req = req;

    /// <summary>Go: <c>cancel context.CancelFunc</c>。</summary>
    internal readonly CancellationTokenSource _cancel = cancel;
}

/// <summary>
/// Go: <c>type Server struct</c>（server.go:170-254）：LSP 服务器。
/// </summary>
public sealed partial class Server : global::TypeScript.Project.Client, global::TypeScript.Project.Ata.NpmExecutor
{
    // 见文件头第 1 条：与 Execute/Build/Orchestrator.cs、Project/Background/Queue.cs 同一个常量。
    private const int _stackSize = 64 * 1024 * 1024;

    // Go: `r Reader` / `w Writer`。
    private readonly Reader _r;
    private readonly Writer _w;

    /// <summary>Go: <c>backgroundCtx context.Context</c>（由 <see cref="Run"/> 设置）。</summary>
    internal Context _backgroundCtx = Context.Background;

    /// <summary>Go: <c>stderr io.Writer</c>（io.Writer → TextWriter，见 Logger.cs 文件头第 4 条）。</summary>
    internal readonly TextWriter _stderr;

    /// <summary>Go: <c>logger *logger</c>。</summary>
    internal readonly Logger _logger;

    /// <summary>Go: <c>initStarted atomic.Bool</c>（volatile 足够：只做 Load/Store）。</summary>
    internal volatile bool _initStarted;

    /// <summary>Go: <c>clientSeq atomic.Int32</c>。</summary>
    private int _clientSeq;

    /// <summary>Go: <c>requestQueue *dynamicQueue[*lsproto.RequestMessage]</c>。</summary>
    private readonly DynamicQueue<RequestMessage> _requestQueue;

    /// <summary>Go: <c>outgoingQueue *dynamicQueue[*lsproto.Message]</c>。</summary>
    internal readonly DynamicQueue<LspMessage> _outgoingQueue;

    /// <summary>Go: <c>pendingClientRequests map[jsonrpc.ID]pendingClientRequest</c>。</summary>
    private readonly Dictionary<JsonRpcID, PendingClientRequest> _pendingClientRequests = [];

    /// <summary>Go: <c>pendingClientRequestsMu sync.Mutex</c>。</summary>
    private readonly Lock _pendingClientRequestsMu = new();

    /// <summary>Go: <c>pendingServerRequests map[jsonrpc.ID]chan *lsproto.ResponseMessage</c>（见文件头第 6 条）。</summary>
    private readonly Dictionary<JsonRpcID, BlockingCollection<ResponseMessage>> _pendingServerRequests = [];

    /// <summary>Go: <c>pendingServerRequestsMu sync.Mutex</c>。</summary>
    private readonly Lock _pendingServerRequestsMu = new();

    private readonly string _cwd;
    private readonly FS _fs;
    private readonly string _defaultLibraryPath;
    private readonly string _typingsLocation;

    /// <summary>Go: <c>initializeParams *lsproto.InitializeParams</c>。</summary>
    private InitializeParams? _initializeParams;

    /// <summary>Go: <c>initializationOptions *lsproto.InitializationOptions</c>。</summary>
    private InitializationOptions _initializationOptions = new();

    /// <summary>Go: <c>clientCapabilities lsproto.ResolvedClientCapabilities</c>。</summary>
    internal ResolvedClientCapabilities _clientCapabilities = new();

    /// <summary>Go: <c>positionEncoding lsproto.PositionEncodingKind</c>。</summary>
    private PositionEncodingKind _positionEncoding;

    /// <summary>Go: <c>localeMu sync.RWMutex</c> + <c>locale locale.Locale</c>。</summary>
    private readonly ReaderWriterLockSlim _localeMu = new();

    private Loc _locale = Loc.Default;

    /// <summary>Go: <c>initLocale locale.Locale</c>：从 initialize 请求解析出来，作为用户偏好为 "auto" 时的回退。</summary>
    private Loc _initLocale = Loc.Default;

    private bool _watchEnabled;
    private bool _telemetryEnabled;

    /// <summary>Go: <c>watchers collections.SyncSet[project.WatcherID]</c>。</summary>
    private readonly SyncSet<global::TypeScript.Project.WatcherID> _watchers = new();

    /// <summary>Go: <c>contentMapperRegistrationMu sync.Mutex</c>。</summary>
    private readonly Lock _contentMapperRegistrationMu = new();

    /// <summary>Go: <c>contentMapperExtensionsRegistered bool</c>。</summary>
    private bool _contentMapperExtensionsRegistered;

    /// <summary>Go: <c>builtinWatcher *lspwatcher.Watcher</c>。</summary>
    private global::TypeScript.LspWatcher.Watcher? _builtinWatcher;

    /// <summary>Go: <c>lastRequestTimeMs atomic.Int64</c>。</summary>
    private long _lastRequestTimeMs;

    /// <summary>Go: <c>session *project.Session</c>。</summary>
    internal ProjectSession? _session;

    /// <summary>Go: <c>client project.Client</c>（测试用的初始化选项；测试直接赋值，Go 侧同理）。</summary>
    internal global::TypeScript.Project.Client? _client;

    /// <summary>Go: <c>initComplete chan struct{}</c>；见 <see cref="InitComplete"/>。</summary>
    private readonly TaskCompletionSource _initComplete = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Go: <c>compilerOptionsForInferredProjects *core.CompilerOptions</c>（临时；见 SetCompilerOptionsForInferredProjects）。</summary>
    private CompilerOptions? _compilerOptionsForInferredProjects;

    /// <summary>Go: <c>parseCache *project.ParseCache</c>。</summary>
    private readonly global::TypeScript.Project.ParseCache? _parseCache;

    private readonly Func<string, string[], byte[]>? _npmInstall;
    private readonly global::TypeScript.ContentMapper.Spawner? _spawn;

    /// <summary>Go: <c>cpuProfiler pprof.CPUProfiler</c>。</summary>
    private readonly global::TypeScript.Execute.CPUProfiler _cpuProfiler = new();

    private readonly TimeSpan _progressDelay;

    /// <summary>Go: <c>projectProgress *projectLoadingProgress</c>。</summary>
    private ProjectLoadingProgress? _projectProgress;

    private readonly Action<int>? _startWatchdog;

    /// <summary>Go: <c>flakeLogging lsproto.DiagnosticFlakeLogLevel</c>。</summary>
    private DiagnosticFlakeLogLevel _flakeLogging = DiagnosticFlakeLogLevel.Off;

    /// <summary>Go 的 <c>g.Wait()</c> 返回值里"真错误"的那部分（见文件头第 5 条）。</summary>
    private Exception? _dispatchExitError;

    /// <summary>Go: <c>func NewServer(opts *ServerOptions) *Server</c>（server.go:56-82）。</summary>
    public Server(ServerOptions opts)
    {
        if (opts.Cwd.Length == 0)
        {
            throw new InvalidOperationException("Cwd is required");
        }

        _r = opts.In;
        _w = opts.Out;
        _stderr = opts.Err;
        _requestQueue = Lsp.NewDynamicQueue<RequestMessage>();
        _outgoingQueue = Lsp.NewDynamicQueue<LspMessage>();
        _cwd = opts.Cwd;
        _fs = opts.FS;
        _defaultLibraryPath = opts.DefaultLibraryPath;
        _typingsLocation = opts.TypingsLocation;
        _parseCache = opts.ParseCache;
        _npmInstall = opts.NpmInstall;
        _spawn = opts.Spawn;
        _startWatchdog = opts.SetParentProcessID;
        _progressDelay = opts.ProgressDelay;
        _logger = Lsp.NewLogger(this);
    }

    /// <summary>Go: <c>var fileRenameFilters</c>（server.go:84-91）。</summary>
    private static readonly FileOperationFilter[] _fileRenameFilters =
    [
        new FileOperationFilter
        {
            Scheme = "file",
            Pattern = new FileOperationPattern
            {
                Glob = "**/*.{ts,tsx,js,jsx,cts,cjs,mts,mjs,json}",
            },
        },
    ];

    /// <summary>Go: <c>func (s *Server) Session() *project.Session</c>（server.go:256-258）→ 只读属性。</summary>
    public ProjectSession? Session => _session;

    /// <summary>
    /// Go: <c>func (s *Server) InitComplete() &lt;-chan struct{}</c>（server.go:235-260 的测试钩子）：
    /// 在 <c>handleInitialized</c> 返回时完成（即初始的能力协商与配置交换都已结束）。
    /// </summary>
    public Task InitComplete => _initComplete.Task;

    /// <summary>Go：<c>close(s.initComplete)</c>（server.go 的 handleInitialized 末尾）。</summary>
    private void SignalInitComplete() => _initComplete.TrySetResult();

    /// <summary>非 Go 源（诊断用）：Go 的 <c>lspExit(err)</c> 记下的"首个退出原因"（见 <see cref="Run"/>）。
    /// 客户端的 <c>InitComplete</c> 等待超时时，用它说明"服务端那侧到底出了什么事"。</summary>
    internal Exception? ExitError => _dispatchExitError;

    /// <summary>Go: <c>func (s *Server) Run(ctx context.Context) error</c>（server.go:900-920）；见文件头第 4 条。</summary>
    public void Run(Context ctx)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken);
        var bgCtx = WithCancellation(ctx, linked.Token);
        _backgroundCtx = bgCtx;

        Exception? dispatchError = null;
        Exception? writeError = null;
        Exception? readError = null;

        // 非 Go 源：超时诊断（把"哪条循环以什么理由退出"记进 HarnessProgress 的事件日志）。
        TestRunner.HarnessProgress.Note("Server.Run: starting dispatch/write/read loops");

        using var readDone = new ManualResetEventSlim(false);
        using var dispatchDone = new ManualResetEventSlim(false);
        using var writeDone = new ManualResetEventSlim(false);
        using var cancelled = new ManualResetEventSlim(false);
        using var cancelRegistration = ctx.CancellationToken.Register(
            () =>
            {
                CancelQuietly(linked);
                cancelled.Set();
            });

        // Go: `g.Go(func() error { return s.dispatchLoop(ctx) })` 与 writeLoop 同：**任一返回/抛错即取消整个组**。
        // 这一点必须显式做：Go 的 errgroup 在任一 goroutine 返回错误时取消派生 ctx，C# 侧没有 errgroup，
        // 漏掉这一步会让"writeLoop 自行退出（例如消息 marshal 失败）"变成**整条会话死锁**（见下面 readLoop 的注释）。
        var dispatch = new Thread(
            () =>
            {
                try
                {
                    DispatchLoop(bgCtx);
                }
                catch (Exception e)
                {
                    dispatchError = e;
                }
                finally
                {
                    TestRunner.HarnessProgress.Note("Server.Run: dispatchLoop exited" + FormatExit(dispatchError));
                    CancelQuietly(linked);
                    dispatchDone.Set();
                }
            },
            _stackSize)
        {
            IsBackground = true,
            Name = "tsgo-lsp-dispatch",
        };
        var write = new Thread(
            () =>
            {
                try
                {
                    WriteLoop(bgCtx);
                }
                catch (Exception e)
                {
                    writeError = e;
                }
                finally
                {
                    TestRunner.HarnessProgress.Note("Server.Run: writeLoop exited" + FormatExit(writeError));
                    CancelQuietly(linked);
                    writeDone.Set();
                }
            },
            _stackSize)
        {
            IsBackground = true,
            Name = "tsgo-lsp-write",
        };

        // Go: `go func() { readLoopErr <- s.readLoop(ctx) }()`——readLoop **不在** errgroup 里（"it blocks on stdin
        // read and cannot be cancelled"），因此它必须跑在自己的线程上，且 Run **不 join 它**。这里是 C# 侧与 Go 的
        // 关键等价点：早先的实现在**调用线程上直接跑 ReadLoop**，于是 writeLoop 一旦先退出（marshal 失败）就没有
        // 任何人取消 readLoop（它阻塞在输入上，取消不了），Run 永远回不来 ⇒ `closeServerOut` 不执行 ⇒ 客户端
        // 的 router 永远读不到 EOF ⇒ 用例线程永远等应答。fourslash 的偶发死锁正是这条路径。
        var read = new Thread(
            () =>
            {
                try
                {
                    ReadLoop(bgCtx);
                }
                catch (Exception e)
                {
                    readError = e;
                }
                finally
                {
                    TestRunner.HarnessProgress.Note("Server.Run: readLoop exited" + FormatExit(readError));
                    CancelQuietly(linked);
                    readDone.Set();
                }
            },
            _stackSize)
        {
            IsBackground = true,
            Name = "tsgo-lsp-read",
        };

        dispatch.Start();
        write.Start();
        read.Start();

        // 非 Go 源：登记给 `--case-timeout` 的看门狗（超时时 dump 这三条循环线程的栈，见 HarnessProgress）。
        TestRunner.HarnessProgress.RegisterThread(dispatch);
        TestRunner.HarnessProgress.RegisterThread(write);
        TestRunner.HarnessProgress.RegisterThread(read);

        // Go: `g.Go(func() error { select { case <-ctx.Done(): return ctx.Err(); case err := <-readLoopErr: return err } })`
        // + `g.Wait()`：三条路径任一结束（读循环出错/EOF、dispatch/write 自行返回、外部取消）就取消其余循环。
        WaitHandle.WaitAny([readDone.WaitHandle, dispatchDone.WaitHandle, writeDone.WaitHandle, cancelled.WaitHandle]);
        CancelQuietly(linked);

        // Go 的 `g.Wait()` 会等 dispatch/write 退出（两者都会在取消后立刻返回）；readLoop 不在组里，不 join。
        if (!dispatchDone.Wait(_loopExitTimeout))
        {
            throw new TimeoutException("LSP dispatch loop did not exit after cancellation");
        }

        if (!writeDone.Wait(_loopExitTimeout))
        {
            throw new TimeoutException("LSP write loop did not exit after cancellation");
        }

        // Go: `if err := g.Wait(); err != nil && !errors.Is(err, io.EOF) && ctx.Err() != nil { return err }`
        // （C# 里 `(T, error)` → 异常，所以这里"返回 err" = 抛；客户端据此取消自己的 ctx，让等待应答的
        //   测试线程立刻失败而不是永远挂着——见 LspClient 的注释。）
        var error = FirstSeriousError(dispatchError, writeError, _dispatchExitError, readError);
        TestRunner.HarnessProgress.Note("Server.Run: returning" + FormatExit(error));
        if (error is not null)
        {
            throw error;
        }
    }

    /// <summary>非 Go 源：给超时诊断的事件日志把"退出原因"格式化成一行。</summary>
    private static string FormatExit(Exception? error) =>
        error is null ? " (no error)" : $" with {error.GetType().Name}: {error.Message}";

    /// <summary>循环退出后的 join 上限：与 Go 的"取消后立即退出"等价，但绝不允许无界等待。</summary>
    private static readonly TimeSpan _loopExitTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Go 的 `cancel()` 在已释放的 CTS 上仍然是安全的 no-op；C# 的 `CancellationTokenSource.Cancel`
    /// 在释放后抛 <see cref="ObjectDisposedException"/>。因为 Run 可能在 readLoop 线程退出**之前**返回
    /// （Go 语义：readLoop 不在 errgroup 里，不 join），那条线程的 finally 会晚于 `Run` 的 `using` 释放，
    /// 所以这里必须静默忽略"已释放"。</summary>
    private static void CancelQuietly(CancellationTokenSource cts)
    {
        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <summary>Go 的 errgroup 会把"第一个非 nil 错误"带出来；`io.EOF`（读到流尾）与 `context.Canceled`
    /// （正常收尾）不算错误，需要过滤掉。</summary>
    private static Exception? FirstSeriousError(params Exception?[] errors)
    {
        foreach (var e in errors)
        {
            if (e is not null && e is not EndOfStreamException && e is not OperationCanceledException)
            {
                return e;
            }
        }

        return null;
    }

    /// <summary>Go: <c>func (s *Server) readLoop(ctx context.Context) error</c>（server.go:922-993）。</summary>
    private void ReadLoop(Context ctx)
    {
        while (true)
        {
            if (ctx.CancellationToken.IsCancellationRequested)
            {
                return;
            }

            LspMessage msg;
            try
            {
                // 非 Go 源：超时诊断（server 的读循环）。
                using var reading = TestRunner.HarnessProgress.Wait("server readLoop reading a message from the transport");
                msg = Read();
            }
            catch (MessageReadException e)
            {
                // Go: `if errors.Is(err, InvalidRequest) || errors.Is(err, InvalidParams)`。
                RequestMessage? req = null;
                if (e.Code == ErrorCode.InvalidParams && e.PartialMessage is not null
                    && e.PartialMessage.Kind == MessageKind.Request)
                {
                    req = e.PartialMessage.AsRequest();
                }

                SendError(req?.ID, e.Code, e.Message);
                continue;
            }

            if (_initializeParams is null && msg.Kind == MessageKind.Request)
            {
                var req = msg.AsRequest();
                if (req.Method == Method.Initialize)
                {
                    InitializeParams? @params;
                    try
                    {
                        @params = req.UnmarshalParams<InitializeParams?>();
                    }
                    catch (ErrorCodeException e)
                    {
                        SendError(req.ID, ErrorCode.InvalidParams, e.Message);
                        continue;
                    }

                    var resp = HandleInitialize(ctx, @params, req);
                    SendResult(req.ID, resp);
                }
                else
                {
                    SendError(req.ID, ErrorCode.ServerNotInitialized, ErrorCode.ServerNotInitialized.String());
                }

                continue;
            }

            if (msg.Kind == MessageKind.Response)
            {
                var resp = msg.AsResponse();
                lock (_pendingServerRequestsMu)
                {
                    if (resp.ID is not null && _pendingServerRequests.TryGetValue(resp.ID, out var respChan))
                    {
                        respChan.Add(resp);
                        respChan.CompleteAdding();
                        _pendingServerRequests.Remove(resp.ID);
                    }
                }
            }
            else
            {
                var req = msg.AsRequest();
                if (req.Method == Method.CancelRequest)
                {
                    var @params = req.UnmarshalParams<CancelParams?>();
                    if (@params is not null)
                    {
                        CancelRequest(@params.Id);
                    }
                }
                else
                {
                    _requestQueue.Put(ctx, req);
                }
            }
        }
    }

    /// <summary>Go: <c>func (s *Server) cancelRequest(rawID lsproto.IntegerOrString)</c>（server.go:995-1003）。</summary>
    private void CancelRequest(IntegerOrString rawID)
    {
        var id = NewID(rawID);
        lock (_pendingClientRequestsMu)
        {
            if (_pendingClientRequests.TryGetValue(id, out var pendingReq))
            {
                pendingReq._cancel.Cancel();
                _pendingClientRequests.Remove(id);
            }
        }
    }

    /// <summary>Go: <c>func (s *Server) read() (*lsproto.Message, error)</c>（server.go:1005-1007）。</summary>
    private LspMessage Read() => _r.Read();

    /// <summary>Go: <c>func (s *Server) dispatchLoop(ctx context.Context) error</c>（server.go:1009-1069）。</summary>
    private void DispatchLoop(Context ctx)
    {
        // Go: `ctx, lspExit := context.WithCancelCause(ctx)`。
        using var loopCts = CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken);
        var loopCtx = WithCancellation(ctx, loopCts.Token);

        while (true)
        {
            RequestMessage req;
            try
            {
                // 非 Go 源：超时诊断。
                using var waitingForRequest = TestRunner.HarnessProgress.Wait("server dispatchLoop waiting for the next queued request");
                req = _requestQueue.Get(loopCtx);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            Interlocked.Exchange(ref _lastRequestTimeMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

            // 见文件头（G1 的 Core.WithRequestID/WithCancellation 不保留 locale，所以 locale 必须最后挂）。
            var requestCtx = Loc.WithLocale(ctx, GetLocale());
            CancellationTokenSource? cancel = null;
            if (req.ID is not null)
            {
                cancel = CancellationTokenSource.CreateLinkedTokenSource(loopCts.Token);
                requestCtx = WithCancellation(WithRequestID(requestCtx, req.ID.String()), cancel.Token);
                lock (_pendingClientRequestsMu)
                {
                    _pendingClientRequests[req.ID] = new PendingClientRequest(req, cancel);
                }
            }

            var requestId = req.ID;
            var requestCancel = cancel;
            var theRequest = req;

            void RemoveRequest()
            {
                if (requestId is not null)
                {
                    requestCancel?.Dispose();
                    lock (_pendingClientRequestsMu)
                    {
                        _pendingClientRequests.Remove(requestId);
                    }
                }
            }

            void HandleError(Exception error)
            {
                if (error is OperationCanceledException)
                {
                    RecordExitError(SendError(requestId, ErrorCode.RequestCancelled, ErrorCode.RequestCancelled.String()));
                }
                else if (error is EndOfStreamException)
                {
                    loopCts.Cancel();
                }
                else
                {
                    RecordExitError(SendError(requestId, ErrorCodeOf(error), error.Message));
                }
            }

            Func<Exception?>? doAsyncWork;
            try
            {
                doAsyncWork = HandleRequestOrNotification(requestCtx, theRequest);
            }
            catch (Exception e)
            {
                HandleError(e);
                RemoveRequest();
                continue;
            }

            if (doAsyncWork is not null)
            {
                // 见文件头第 1 条：异步段派发到显式 64 MiB 栈的线程。
                var work = doAsyncWork;
                var thread = new Thread(
                    () =>
                    {
                        try
                        {
                            var lsError = work();
                            if (lsError is not null)
                            {
                                HandleError(lsError);
                            }
                        }
                        finally
                        {
                            RemoveRequest();
                        }
                    },
                    _stackSize)
                {
                    IsBackground = true,
                    Name = "tsgo-lsp-request",
                };
                // 非 Go 源：登记给 `--case-timeout` 的看门狗（超时诊断）。
                TestRunner.HarnessProgress.RegisterThread(thread);
                thread.Start();
            }
            else
            {
                RemoveRequest();
            }
        }
    }

    /// <summary>Go 的 <c>lspExit(err)</c>（server.go:1022-1034）：记录"首个"退出原因并取消循环。</summary>
    private void RecordExitError(Exception? error)
    {
        if (error is not null)
        {
            Interlocked.CompareExchange(ref _dispatchExitError, error, null);
        }
    }

    /// <summary>Go: <c>func (s *Server) writeLoop(ctx context.Context) error</c>（server.go:1071-1091）。</summary>
    private void WriteLoop(Context ctx)
    {
        while (true)
        {
            LspMessage msg;
            try
            {
                // 非 Go 源：超时诊断。
                using var waitingForMessage = TestRunner.HarnessProgress.Wait("server writeLoop waiting for the next outgoing message");
                msg = _outgoingQueue.Get(ctx);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                _w.Write(msg);
            }
            catch (Exception writeFailure)
            {
                // Go 的 `if err := s.w.Write(msg); err != nil`：**任何**写失败都走这条分支（不只是 marshal 失败），
                // 因为 `_w.Write` 还可能因为"对端已关闭"而抛（C# 的 BlockingPipeStream 在写侧关闭后 Add 抛
                // InvalidOperationException，Go 的 io.Pipe 返回 io.ErrClosedPipe）。不捕获会让写线程带着未处理
                // 异常死掉——.NET 里那是**进程崩溃**，比这里的"记录错误并退出循环"糟糕得多。
                if (writeFailure is MessageMarshalError marshalErr && msg.Kind == MessageKind.Response)
                {
                    var resp = msg.AsResponse();
                    if (resp.ID is not null && resp.Error is null)
                    {
                        _logger.Errorf(
                            "failed to marshal response for request {0}: {1}",
                            resp.ID.String(),
                            marshalErr.Outer.Message);
                        if (SendError(resp.ID, ErrorCode.InternalError, marshalErr.Message) is { } sendErr)
                        {
                            RecordExitError(sendErr);
                            return;
                        }

                        continue;
                    }
                }

                // Go: `return fmt.Errorf("failed to write message: %w", err)`——%w 会把内层错误文本也带进
                // 消息（"failed to write message: failed to marshal message: …"），这里照抄同样的文本，
                // 否则 harness 只看到一句无信息量的 "failed to write message"。
                RecordExitError(new IOException($"failed to write message: {writeFailure.Message}", writeFailure));
                return;
            }
        }
    }

    /// <summary>
    /// Go: <c>func (s *Server) sendClientRequest[Req, Resp any](ctx, info, params) (Resp, error)</c>（server.go:1096-1132）。
    /// **警告**：只能在 handler 的异步段调用，否则会死锁。
    /// </summary>
    internal (Resp? Result, Exception? Error) SendClientRequest<Req, Resp>(Context ctx, RequestInfo<Req, Resp> info, Req @params)
    {
        var id = JsonRpcID.NewIDString($"ts{Interlocked.Increment(ref _clientSeq)}");
        var req = info.NewRequestMessage(id, @params);

        var responseChan = new BlockingCollection<ResponseMessage>(1);
        lock (_pendingServerRequestsMu)
        {
            _pendingServerRequests[id] = responseChan;
        }

        try
        {
            Send(req.Message());

            try
            {
                // 非 Go 源：超时诊断（server 等客户端应答；应答丢了就永远等下去）。
                using var waiting = TestRunner.HarnessProgress.Wait($"server waiting for client response to {id}");
                if (!responseChan.TryTake(out var resp, Timeout.Infinite, ctx.CancellationToken))
                {
                    return (default, new OperationCanceledException(ctx.CancellationToken));
                }

                if (resp.Error is not null)
                {
                    return (default, new InvalidOperationException($"request failed: {resp.Error.String()}"));
                }

                return (info.UnmarshalResult(resp.Result), null);
            }
            catch (OperationCanceledException)
            {
                return (default, new OperationCanceledException(ctx.CancellationToken));
            }
        }
        finally
        {
            lock (_pendingServerRequestsMu)
            {
                if (_pendingServerRequests.TryGetValue(id, out var existing))
                {
                    existing.CompleteAdding();
                    _pendingServerRequests.Remove(id);
                }
            }
        }
    }

    /// <summary>
    /// Go: <c>func (s *Server) sendClientRequestFireAndForget[Req, Resp any](info, params) error</c>（server.go:1137-1142）。
    /// 响应（如果有）会被 readLoop 静默忽略（没有登记等待 channel）。
    /// </summary>
    internal Exception? SendClientRequestFireAndForget<Req, Resp>(RequestInfo<Req, Resp> info, Req @params)
    {
        var id = JsonRpcID.NewIDString($"ts{Interlocked.Increment(ref _clientSeq)}");
        var req = info.NewRequestMessage(id, @params);
        try
        {
            Send(req.Message());
            return null;
        }
        catch (Exception e)
        {
            return e;
        }
    }

    /// <summary>Go: <c>func (s *Server) sendResult(id *jsonrpc.ID, result any) error</c>（server.go:1144-1148）。</summary>
    internal Exception? SendResult(JsonRpcID? id, object? result) =>
        SendResponse(new ResponseMessage { ID = id, Result = result });

    /// <summary>Go: <c>type userFacingRequestFailedError string</c>（server.go:1150-1155）。</summary>
    internal sealed class UserFacingRequestFailedError(string message) : Exception(message)
    {
        internal readonly ErrorCode Code = ErrorCode.RequestFailed;
    }

    /// <summary>从异常里取出 Go 的 <c>errors.AsType[lsproto.ErrorCode]</c> 结果（默认 InternalError）。</summary>
    private static ErrorCode ErrorCodeOf(Exception error) => error switch
    {
        ErrorCodeException e => e.Code,
        UserFacingRequestFailedError e => e.Code,
        MessageMarshalError e => e.Code,
        _ => ErrorCode.InternalError,
    };

    /// <summary>Go: <c>func (s *Server) sendError(id *jsonrpc.ID, err error) error</c>（server.go:1157-1176）。</summary>
    internal Exception? SendError(JsonRpcID? id, Exception err) => SendError(id, ErrorCodeOf(err), err.Message);

    /// <summary>Go 的 <c>sendError</c>，错误码显式给出（C# 侧的错误对象不必承载错误码）。</summary>
    internal Exception? SendError(JsonRpcID? id, ErrorCode code, string message)
    {
        // Do not send error response for notifications,
        // except for parse errors which may occur before determining if the message is a request or notification.
        if (id is null && code != ErrorCode.InvalidRequest)
        {
            _logger.Errorf("error handling notification: {0}", message);
            return null;
        }

        // TODO(jakeebailey): error data
        return SendResponse(new ResponseMessage
        {
            ID = id,
            Error = new JsonRpcResponseError
            {
                Code = (int)code,
                Message = message,
            },
        });
    }

    /// <summary>Go: <c>func (s *Server) sendNotification[Params any](info, params) error</c>（server.go:1178-1180）。</summary>
    internal Exception? SendNotification<Params>(NotificationInfo<Params> info, Params @params) =>
        Send(info.NewNotificationMessage(@params).Message());

    /// <summary>Go: <c>func (s *Server) sendResponse(resp *lsproto.ResponseMessage) error</c>（server.go:1182-1184）。</summary>
    private Exception? SendResponse(ResponseMessage resp) => Send(resp.Message());

    /// <summary>Go: <c>func (s *Server) send(msg *lsproto.Message) error</c>（server.go:1186-1189）：
    /// 把消息放进发出队列，尊重 ctx 取消。</summary>
    private Exception? Send(LspMessage msg)
    {
        try
        {
            _outgoingQueue.Put(_backgroundCtx, msg);
            return null;
        }
        catch (OperationCanceledException)
        {
            return new OperationCanceledException(_backgroundCtx.CancellationToken);
        }
    }

    /// <summary>
    /// Go: <c>func (s *Server) handleRequestOrNotification(ctx, req) (func() error, error)</c>（server.go:1191-1245）：
    /// 查出该方法对应的 handler，执行它的同步段，并把异步段作为委托返回。
    /// 同步段的 error 在 C# 里是**异常**（见文件头第 4 条），这里就地做 Go 的日志/回退处理后再抛给调用方。
    /// </summary>
    private Func<Exception?>? HandleRequestOrNotification(Context ctx, RequestMessage req)
    {
        WithClientCapabilities(_clientCapabilities);

        var handler = Handlers().GetValueOrDefault(req.Method);
        if (handler is null)
        {
            _logger.Warn("unknown method '", req.Method.Value, "'");
            if (req.ID is not null)
            {
                SendError(req.ID, ErrorCode.InvalidRequest, ErrorCode.InvalidRequest.String());
            }

            return null;
        }

        var start = DateTime.UtcNow;
        var idStr = req.ID is not null ? " (" + req.ID.String() + ")" : "";

        Func<Exception?>? doAsyncWork;
        try
        {
            doAsyncWork = handler(this, ctx, req);
        }
        catch (Exception err)
        {
            var (fallbackResp, ok) = ContentMapperFallbackResponse(req.Method, err);
            if (ok)
            {
                if (!_logger.IsTracing())
                {
                    _logger.Info("handled method '", req.Method.Value, "'", idStr, " in ", DateTime.UtcNow - start);
                }

                if (SendResult(req.ID, fallbackResp) is { } sendErr)
                {
                    throw sendErr;
                }

                return null;
            }

            if (err is not UserFacingRequestFailedError)
            {
                _logger.Error("error handling method '", req.Method.Value, "'", idStr, ": ", err.Message);
            }
            else if (!_logger.IsTracing())
            {
                _logger.Info("handled method '", req.Method.Value, "'", idStr, " in ", DateTime.UtcNow - start);
            }

            throw;
        }

        if (doAsyncWork is not null)
        {
            return () =>
            {
                // note: ctx.Err() has to be checked in the async work to allow async handlers to cleanup resources correctly
                var asyncWorkErr = doAsyncWork();
                var isUserFacing = asyncWorkErr is UserFacingRequestFailedError;
                var isRealError = asyncWorkErr is not null && !isUserFacing;
                if (isRealError)
                {
                    _logger.Info("error handling method '", req.Method.Value, "'", idStr, " in ", DateTime.UtcNow - start);
                }
                else if (!_logger.IsTracing())
                {
                    _logger.Info("handled method '", req.Method.Value, "'", idStr, " in ", DateTime.UtcNow - start);
                }

                return asyncWorkErr;
            };
        }

        if (!_logger.IsTracing())
        {
            _logger.Info("handled method '", req.Method.Value, "'", idStr, " in ", DateTime.UtcNow - start);
        }

        return null;
    }

    /// <summary>Go: <c>func (s *Server) recover(req *lsproto.RequestMessage)</c>（server.go:1443-1470）。</summary>
    internal void Recover(RequestMessage req, Exception error)
    {
        var method = req.Method.Value;
        var stack = Environment.StackTrace;
        _logger.Errorf("panic handling request {0}: {1}\n{2}", method, error.Message, stack);
        if (req.ID is not null)
        {
            SendError(req.ID, ErrorCode.InternalError, $"{ErrorCode.InternalError.String()}: panic handling request {method}: {error.Message}");
        }
        else
        {
            _logger.Error("unhandled panic in notification", method, error.Message);
        }

        if (_telemetryEnabled)
        {
            _ = SendNotification(TelemetryEventInfo, new RequestFailureTelemetryEventOrPerformanceStatsTelemetryEventOrProjectInfoTelemetryEventOrNull
            {
                RequestFailureTelemetryEvent = new RequestFailureTelemetryEvent
                {
                    Properties = new RequestFailureTelemetryProperties
                    {
                        ErrorCode = ErrorCode.InternalError.String(),
                        RequestMethod = method.Replace("/", "."),
                        Stack = Lsp.SanitizeStackTrace(stack),
                    },
                },
            });
        }
    }
}
