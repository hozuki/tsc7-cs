// ported from: internal/execute/tsc/compile.go
//
// 与 Go 的偏离（docs/architecture.md §2/§3）：
// 1) Go 的 `System` 接口名与根命名空间 `System` 同名（同 §2 的 `TypeScript.Debug` 坑）：本命名空间内一律
//    `using global::System;` / `using global::System.IO;`，且**禁止** `System.` 前缀 —— 否则名字解析会先绑到
//    本接口（CS0426）。
// 2) `io.Writer` → `System.IO.TextWriter`（DiagnosticWriter 既有约定：Go 的 `fmt.Fprint` → `TextWriter.Write`）。
// 3) `context.Context` → `TypeScript.Core.Context`（`ctx.Done()` → `ctx.CancellationToken`）。
// 4) `GetEnvironmentVariable(name) (string, bool)` → `TryGetEnvironmentVariable(name, out value)`（§3.2）。
// 5) `Spawn(command, dir, stderr) (io.ReadWriteCloser, error)` → 显式继承 `ContentMapper.Spawner`（Go 里 System
//    结构性地满足 Spawner；C# 必须把这条关系写出来，实现仍是 `Stream Spawn(string[], string, Stream)`）。
// 6) Go 的 `time.Duration` → `TimeSpan`，`time.Time` → `DateTime`。
// 7) `CompileTimes` 的未导出字段 → `internal` + `_camelCase`（Statistics 在同一个程序集里访问）。
// 8) Go 的无名函数类型 `func(msg *diagnostics.Message, args ...any)` → `TraceFunc` 委托。
// 9) Go 里 `System` 结构性地满足 `tsoptions.ParseConfigHost`（`ParseCommandLine(args, sys)` 就靠这个）→ C# 必须把
//    这条关系写出来：`System : Spawner, ParseConfigHost`（同偏离 5）；Go 的 `CompilerHost` 同样自己又列了一遍
//    `FS()`/`GetCurrentDirectory()`，C# 里按 Compiler/Host.cs 的既有做法用 `new` 显式隐藏（保住 Go 的声明面）。
// 10) Go 的 `config.Locale()` 在 C# 是属性 `ParsedCommandLine.Locale`。
using global::System;
using TextWriter = global::System.IO.TextWriter;
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Compiler;
using TypeScript.ContentMapper;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.TSPath;
using TypeScript.Vfs;
using Loc = TypeScript.Locale.Locale;
using CM = TypeScript.ContentMapper.ContentMapper;
using Path = TypeScript.TSPath.Path;
using AstDiag = TypeScript.Ast.Diagnostic;
using static TypeScript.TsOptions.TsOptions;

namespace TypeScript.Execute.Tsc;

/// <summary>Go: <c>type System interface</c>（compile.go:22-35）。</summary>
public interface System : Spawner, ParseConfigHost
{
    /// <summary>Go: <c>Writer() io.Writer</c>。</summary>
    TextWriter Writer();

    /// <summary>Go: <c>ErrorWriter() io.Writer</c>。</summary>
    TextWriter ErrorWriter();

    /// <summary>Go: <c>FS() vfs.FS</c>。</summary>
    new FS FS();

    /// <summary>Go: <c>DefaultLibraryPath() string</c>。</summary>
    string DefaultLibraryPath();

    /// <summary>Go: <c>GetCurrentDirectory() string</c>。</summary>
    new string GetCurrentDirectory();

    /// <summary>Go: <c>WriteOutputIsTTY() bool</c>。</summary>
    bool WriteOutputIsTTY();

    /// <summary>Go: <c>GetWidthOfTerminal() int</c>。</summary>
    int GetWidthOfTerminal();

    /// <summary>Go: <c>GetEnvironmentVariable(name string) (string, bool)</c>（§3.2：<c>(T, bool)</c> → <c>TryX</c>）。</summary>
    bool TryGetEnvironmentVariable(string name, out string value);

    /// <summary>Go: <c>Now() time.Time</c>。</summary>
    DateTime Now();

    /// <summary>Go: <c>SinceStart() time.Duration</c>。</summary>
    TimeSpan SinceStart();
}

/// <summary>Go: <c>func newContentMapperLogger(sys System) contentmapper.Logger</c>（compile.go:37-48）。</summary>
internal static class ContentMapperLogger
{
    public static Logger? New(System sys)
    {
        if (!sys.TryGetEnvironmentVariable("TS_CONTENT_MAPPER_DEBUG", out var value) || value == "")
        {
            return null;
        }
        var writer = sys.ErrorWriter();
        var mu = new object();
        return message =>
        {
            lock (mu)
            {
                writer.WriteLine(message);
            }
        };
    }
}

/// <summary>Go: <c>type ExitStatus int</c>（compile.go:50-59）。</summary>
public enum ExitStatus
{
    Success = 0,
    DiagnosticsPresent_OutputsSkipped = 1,
    DiagnosticsPresent_OutputsGenerated = 2,
    InvalidProject_OutputsSkipped = 3,
    ProjectReferenceCycle_OutputsSkipped = 4,
    NotImplemented = 5,
}

/// <summary>Go: <c>type Watcher interface</c>（compile.go:61-63）。</summary>
public interface Watcher
{
    /// <summary>Go: <c>DoCycle()</c>。</summary>
    void DoCycle();
}

/// <summary>Go: <c>type CommandLineResult struct</c>（compile.go:65-68）。</summary>
public sealed class CommandLineResult
{
    /// <summary>Go: <c>Status ExitStatus</c>。</summary>
    public ExitStatus Status;

    /// <summary>Go: <c>Watcher Watcher</c>（nil = 非 watch 模式）。</summary>
    public Watcher? Watcher;
}

/// <summary>Go: <c>func(msg *diagnostics.Message, args ...any)</c>（compile.go:80 的匿名返回类型）。</summary>
public delegate void TraceFunc(Message msg, params object?[] args);

/// <summary>Go: <c>type CommandLineTesting interface</c>（compile.go:70-87）。</summary>
public interface CommandLineTesting
{
    /// <summary>Go: <c>OnEmittedFiles(result *compiler.EmitResult, mTimesCache *collections.SyncMap[tspath.Path, time.Time])</c>。</summary>
    void OnEmittedFiles(EmitResult? result, SyncMap<Path, DateTime>? mTimesCache);

    /// <summary>Go: <c>OnListFilesStart(w io.Writer)</c>。</summary>
    void OnListFilesStart(TextWriter w);

    /// <summary>Go: <c>OnListFilesEnd(w io.Writer)</c>。</summary>
    void OnListFilesEnd(TextWriter w);

    /// <summary>Go: <c>OnStatisticsStart(w io.Writer)</c>。</summary>
    void OnStatisticsStart(TextWriter w);

    /// <summary>Go: <c>OnStatisticsEnd(w io.Writer)</c>。</summary>
    void OnStatisticsEnd(TextWriter w);

    /// <summary>Go: <c>OnBuildStatusReportStart(w io.Writer)</c>。</summary>
    void OnBuildStatusReportStart(TextWriter w);

    /// <summary>Go: <c>OnBuildStatusReportEnd(w io.Writer)</c>。</summary>
    void OnBuildStatusReportEnd(TextWriter w);

    /// <summary>Go: <c>OnWatchStatusReportStart()</c>。</summary>
    void OnWatchStatusReportStart();

    /// <summary>Go: <c>OnWatchStatusReportEnd()</c>。</summary>
    void OnWatchStatusReportEnd();

    /// <summary>Go: <c>GetTrace(w io.Writer, locale locale.Locale) func(msg *diagnostics.Message, args ...any)</c>。</summary>
    TraceFunc GetTrace(TextWriter w, Loc locale);

    /// <summary>Go: <c>OnProgram(program *incremental.Program)</c>。</summary>
    void OnProgram(TypeScript.Execute.Incremental.Program program);
}

/// <summary>
/// Go: <c>func NewContentMapperHost(ctx, sys System, options *core.CompilerOptions) contentmapper.Host</c>
/// （compile.go:89-97）。启用 `--runExternalCode` 时创建 content mapper host（通过 <c>sys.Spawn</c> 拉起 mapper 进程），
/// 否则返回 nil（此时无法加载 content-mapped 文件）。调用方拥有 host，会话结束时要 <c>Close</c>。
/// </summary>
public static partial class Tsc
{
    public static Host? NewContentMapperHost(Context ctx, System sys, CompilerOptions options)
    {
        if (!options.RunExternalCode.IsTrue())
        {
            return null;
        }
        Loc.TryParse(options.Locale, out var diagnosticLocale);
        return CM.NewHostWithOptions(ctx, sys, diagnosticLocale, new HostOptions
        {
            Logger = ContentMapperLogger.New(sys),
        });
    }
}

/// <summary>Go: <c>type CompileTimes struct</c>（compile.go:99-109）。</summary>
public sealed class CompileTimes
{
    /// <summary>Go: <c>ConfigTime time.Duration</c>。</summary>
    public TimeSpan ConfigTime;

    /// <summary>Go: <c>ParseTime time.Duration</c>。</summary>
    public TimeSpan ParseTime;

    /// <summary>Go: <c>ContentMapperTimes contentmapper.Timings</c>（Go 的零值结构体 → C# 需要实例）。</summary>
    public Timings ContentMapperTimes = new();

    /// <summary>Go: <c>bindTime time.Duration</c>（未导出）。</summary>
    internal TimeSpan _bindTime;

    /// <summary>Go: <c>checkTime time.Duration</c>（未导出）。</summary>
    internal TimeSpan _checkTime;

    /// <summary>Go: <c>totalTime time.Duration</c>（未导出）。</summary>
    internal TimeSpan _totalTime;

    /// <summary>Go: <c>emitTime time.Duration</c>（未导出）。</summary>
    internal TimeSpan _emitTime;

    /// <summary>Go: <c>BuildInfoReadTime time.Duration</c>。</summary>
    public TimeSpan BuildInfoReadTime;

    /// <summary>Go: <c>ChangesComputeTime time.Duration</c>。</summary>
    public TimeSpan ChangesComputeTime;
}

/// <summary>Go: <c>type CompileAndEmitResult struct</c>（compile.go:110-115）。</summary>
public sealed class CompileAndEmitResult
{
    /// <summary>Go: <c>Diagnostics []*ast.Diagnostic</c>。</summary>
    public AstDiag[] Diagnostics = [];

    /// <summary>Go: <c>EmitResult *compiler.EmitResult</c>。</summary>
    public EmitResult? EmitResult;

    /// <summary>Go: <c>Status ExitStatus</c>。</summary>
    public ExitStatus Status;

    /// <summary>Go: <c>times *CompileTimes</c>（未导出）。</summary>
    internal CompileTimes? _times;
}
