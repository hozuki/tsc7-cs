// ported from: internal/lsp/logger.go:1-185（整文件）
//
// 与 Go 的偏离（逐条说明）：
//
// 1) Go 的 `logger` 的每个方法都以 `if l == nil { return }` 兜底（`newLogger` 永远返回非 nil，但接口值
//    里可能装 nil）。C# 的实例方法不能在 null 引用上分派，这些分支不可达 ⇒ 删掉；可观察行为不变
//    （先例：`Project/Logging/Logger.cs` 文件头第 2 条）。
//
// 2) Go 包级函数 `newLogger` / `maxVerbosityForMessageType` / `isValidLogVerbosity` → 静态类 `Lsp` 的
//    `internal static NewLogger/MaxVerbosityForMessageType/IsValidLogVerbosity`（Go 包级函数 → 同名静态类，
//    见 G1 约定）。Go 的 `logger` 类型 → C# 的 `Logger` 类。
//
// 3) `fmt.Sprint(msg...)` → `Project.Logging.LoggerImpl.Sprint(msg)`（本仓已有的 `fmt.Sprint` 复刻，
//    内部是 `internal static`，同程序集可用；不另写一份以免两处格式规则漂移）。
//    `fmt.Sprintf(format, args...)` → `string.Format(CultureInfo.InvariantCulture, format, args)`
//    ——**格式串由调用点改写**（Go 的 `%v`/`%d` → C# 的 `{0}`），本仓既有约定（`Project/Logging/Logger.cs`
//    文件头第 4 条）。
//
// 4) `fmt.Fprintln(l.server.stderr, message)` → `_server._stderr.Write(message + "\n")`：恒写 `'\n'`
//    （不能用 `TextWriter.WriteLine`，Windows 上它会写 `\r\n`，见 `Project/Logging/Logger.cs` 文件头第 1 条）。
//
// 5) `l.server.outgoingQueue.Put(ctx, msg)` 在 Go 里返回 error（只可能是 ctx 取消）→ C# 抛
//    `OperationCanceledException`；Go 的 `if l.server.backgroundCtx.Err() != nil` 兜底日志在 C# 里是
//    `catch (OperationCanceledException)` 后的同一个判断。
//
// 6) `sync.Mutex` → `System.Threading.Lock`（本仓统一约定）。
//
// 7) 本类实现 `TypeScript.Project.Logging.Logger`（Go: `var _ logging.Logger = (*logger)(nil)`）；
//    另有两个 lsp 专有的访问器 `IsTracing()` / `SetVerbosity(LogVerbosity)`（Go 侧同理不在接口里）。
using System.Globalization;
using TypeScript.LsProto;
using static TypeScript.LsProto.LsProto;
using ProjectLogger = TypeScript.Project.Logging.Logger;

namespace TypeScript.Lsp;

/// <summary>
/// Go: <c>type logger struct</c>（logger.go:12-16）：把日志转发成
/// <c>window/logMessage</c> 通知的 <c>logging.Logger</c> 实现。
/// </summary>
public sealed class Logger : ProjectLogger
{
    private readonly Server _server;
    private readonly Lock _mu = new();

    /// <summary>Go: <c>verbosity lsproto.LogVerbosity</c>。</summary>
    private LogVerbosity _verbosity;

    internal Logger(Server server)
    {
        _server = server;
        _verbosity = LogVerbosity.Info;
    }

    /// <summary>Go: <c>func (l *logger) sendLogMessage(msgType lsproto.MessageType, message string)</c>（logger.go:44-69）。</summary>
    private void SendLogMessage(MessageType msgType, string message)
    {
        if (!_server._initStarted)
        {
            _server._stderr.Write(message + "\n");
            return;
        }

        // Don't send messages that the client will filter out anyway.
        LogVerbosity verbosity;
        lock (_mu)
        {
            verbosity = _verbosity;
        }

        if (verbosity == LogVerbosity.Off || verbosity > Lsp.MaxVerbosityForMessageType(msgType))
        {
            return;
        }

        var notification = WindowLogMessageInfo.NewNotificationMessage(new LogMessageParams
        {
            Type = msgType,
            Message = message,
        });

        try
        {
            _server._outgoingQueue.Put(_server._backgroundCtx, notification.Message());
        }
        catch (OperationCanceledException)
        {
            if (_server._backgroundCtx.CancellationToken.IsCancellationRequested)
            {
                _server._stderr.Write(message + "\n");
            }
        }
    }

    /// <summary>Go: <c>func (l *logger) Log(msg ...any)</c>（logger.go:71-75）。</summary>
    public void Log(params object?[] msg) => SendLogMessage(MessageType.Info, Project.Logging.LoggerImpl.Sprint(msg));

    /// <summary>Go: <c>func (l *logger) Logf(format string, args ...any)</c>（logger.go:77-81）。</summary>
    public void Logf(string format, params object?[] args) =>
        SendLogMessage(MessageType.Info, string.Format(CultureInfo.InvariantCulture, format, args));

    /// <summary>Go: <c>func (l *logger) Verbose() logging.Logger</c>（logger.go:83-92）。</summary>
    public ProjectLogger? Verbose()
    {
        lock (_mu)
        {
            if (_verbosity == LogVerbosity.Off || _verbosity > LogVerbosity.Debug)
            {
                return null;
            }

            return this;
        }
    }

    /// <summary>Go: <c>func (l *logger) IsVerbose() bool</c>（logger.go:94-100）。</summary>
    public bool IsVerbose()
    {
        lock (_mu)
        {
            return _verbosity >= LogVerbosity.Trace && _verbosity <= LogVerbosity.Debug;
        }
    }

    /// <summary>Go: <c>func (l *logger) SetVerbose(verbose bool)</c>（logger.go:102-111）。</summary>
    public void SetVerbose(bool verbose)
    {
        lock (_mu)
        {
            _verbosity = verbose ? LogVerbosity.Debug : LogVerbosity.Info;
        }
    }

    /// <summary>Go: <c>func (l *logger) IsTracing() bool</c>（logger.go:113-119）。</summary>
    public bool IsTracing()
    {
        lock (_mu)
        {
            return _verbosity == LogVerbosity.Trace;
        }
    }

    /// <summary>Go: <c>func (l *logger) SetVerbosity(verbosity lsproto.LogVerbosity)</c>（logger.go:121-127）。</summary>
    public void SetVerbosity(LogVerbosity verbosity)
    {
        lock (_mu)
        {
            _verbosity = verbosity;
        }
    }

    /// <summary>Go: <c>func (l *logger) Error(msg ...any)</c>（logger.go:129-133）。</summary>
    public void Error(params object?[] msg) => SendLogMessage(MessageType.Error, Project.Logging.LoggerImpl.Sprint(msg));

    /// <summary>Go: <c>func (l *logger) Errorf(format string, args ...any)</c>（logger.go:135-139）。</summary>
    public void Errorf(string format, params object?[] args) =>
        SendLogMessage(MessageType.Error, string.Format(CultureInfo.InvariantCulture, format, args));

    /// <summary>Go: <c>func (l *logger) Warn(msg ...any)</c>（logger.go:141-145）。</summary>
    public void Warn(params object?[] msg) => SendLogMessage(MessageType.Warning, Project.Logging.LoggerImpl.Sprint(msg));

    /// <summary>Go: <c>func (l *logger) Warnf(format string, args ...any)</c>（logger.go:147-151）。</summary>
    public void Warnf(string format, params object?[] args) =>
        SendLogMessage(MessageType.Warning, string.Format(CultureInfo.InvariantCulture, format, args));

    /// <summary>Go: <c>func (l *logger) Info(msg ...any)</c>（logger.go:153-157）。</summary>
    public void Info(params object?[] msg) => SendLogMessage(MessageType.Info, Project.Logging.LoggerImpl.Sprint(msg));

    /// <summary>Go: <c>func (l *logger) Infof(format string, args ...any)</c>（logger.go:159-163）。</summary>
    public void Infof(string format, params object?[] args) =>
        SendLogMessage(MessageType.Info, string.Format(CultureInfo.InvariantCulture, format, args));
}

/// <summary>Go 的 <c>internal/lsp</c> 包级函数（本文件只有 logger.go 的三个）。</summary>
public static partial class Lsp
{
    /// <summary>Go: <c>func newLogger(server *Server) *logger</c>（logger.go:18-23）。</summary>
    internal static Logger NewLogger(Server server) => new(server);

    /// <summary>
    /// Go: <c>func maxVerbosityForMessageType(msgType lsproto.MessageType) lsproto.LogVerbosity</c>
    /// （logger.go:25-38）：给定消息类型"至少要多啰嗦的级别才该发出去"。
    /// </summary>
    internal static LogVerbosity MaxVerbosityForMessageType(MessageType msgType) => msgType switch
    {
        MessageType.Error => LogVerbosity.Error,
        MessageType.Warning => LogVerbosity.Warning,
        MessageType.Info => LogVerbosity.Info,
        MessageType.Debug => LogVerbosity.Debug,
        _ => LogVerbosity.Info,
    };

    /// <summary>Go: <c>func isValidLogVerbosity(v lsproto.LogVerbosity) bool</c>（logger.go:40-42）。</summary>
    internal static bool IsValidLogVerbosity(LogVerbosity v) => v >= LogVerbosity.Off && v <= LogVerbosity.Error;
}

