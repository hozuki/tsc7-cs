// ported from: internal/contentmapper/host.go
//
// C# 映射与偏离（逐条）：
// 1) Go 的 error 类型（`TransformError`/`DiagnosticDirectiveError`/`InvalidVirtualExtensionError`/`ProjectError`/
//    `InitializeError`/`SupplementalFileCollisionError`）→ C# 的**异常类型**（docs/architecture.md §3.2：Go 的
//    error → C# 异常，`errors.AsType`/`errors.Is` → `catch`）。`Error() string` 的文本进 `Message`。
// 2) `NewTransformError(kind, err) *TransformError` 保留为静态工厂（公开 API 对等）。
// 3) `time.Duration` → `System.TimeSpan`（`int64` 纳秒语义一致；`OperationTiming.Duration` 用 TimeSpan）。
// 4) `Timings.Since(previous)` 是值方法（Go 的值接收者）→ C# 实例方法，返回新的 `Timings`。
// 5) `Project` / `Host` 是 Go 接口（导出）→ C# 同名接口（不加 I 前缀，与 ast.SourceFileLike/HasFileName 一致）。
// 6) `(T, error)` → 正常返回 T，失败抛异常（`Identities()`/`Identity()`/`WatchedFiles()`/`Transform()`/`Close()`）。
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.SpanMap;
using TypeScript.TSPath;

namespace TypeScript.ContentMapper;

/// <summary>Go: <c>type TransformErrorKind uint8</c>（数值照 Go 的 iota）。</summary>
public enum TransformErrorKind : byte
{
    Unknown = 0,
    Initialize = 1,
    Project = 2,
    Request = 3,
    Response = 4,
    Mappings = 5,
}

/// <summary>Go: <c>type TransformError struct</c>（准备/请求/解码变换时的失败）。</summary>
public sealed class TransformError : Exception
{
    public TransformErrorKind Kind;

    public TransformError(TransformErrorKind kind, Exception? err)
        : base($"content mapper transform failed: {err?.Message}", err)
    {
        Kind = kind;
    }
}

/// <summary>Go: <c>type DiagnosticDirectiveErrorKind uint8</c>。</summary>
public enum DiagnosticDirectiveErrorKind : byte
{
    InvalidRange = 0,
    InvalidPolicy = 1,
    ExpectMissingUnusedDiagnostic = 2,
    InvalidUnusedDiagnosticIndex = 3,
    Overlap = 4,
}

/// <summary>Go: <c>type DiagnosticDirectiveError struct</c>（变换响应里的诊断指令不合法）。</summary>
public sealed class DiagnosticDirectiveError : Exception
{
    public DiagnosticDirectiveErrorKind Kind;
    public int Index;
    public int SupplementalIndex;
    public DiagnosticDirectivePolicy Policy;

    public DiagnosticDirectiveError(DiagnosticDirectiveErrorKind kind, int index, int supplementalIndex)
        : base($"invalid content mapper diagnostic directive {index}")
    {
        Kind = kind;
        Index = index;
        SupplementalIndex = supplementalIndex;
    }
}

/// <summary>Go: <c>type InvalidVirtualExtensionError struct</c>（映射输出的虚拟扩展名缺失或不受支持）。</summary>
public sealed class InvalidVirtualExtensionError(string extension) : Exception($"invalid virtual extension \"{extension}\"")
{
    public string Extension = extension;
}

/// <summary>Go: <c>type ProjectErrorKind uint8</c>。</summary>
public enum ProjectErrorKind : byte
{
    MalformedResponse = 0,
    MissingConfigIdentity = 1,
    NonAbsoluteWatchedFile = 2,
    UnexpectedConfigIdentity = 3,
    UnexpectedWatchedFiles = 4,
}

/// <summary>Go: <c>type ProjectError struct</c>（mapper 的 openProject 响应不合法）。</summary>
public sealed class ProjectError : Exception
{
    public ProjectErrorKind Kind;

    public ProjectError(ProjectErrorKind kind)
        : base(MessageFor(kind))
    {
        Kind = kind;
    }

    private static string MessageFor(ProjectErrorKind kind) => kind switch
    {
        ProjectErrorKind.MalformedResponse => "content mapper returned a malformed project response",
        ProjectErrorKind.MissingConfigIdentity => "content mapper did not return configIdentity for dynamic configuration",
        ProjectErrorKind.NonAbsoluteWatchedFile => "content mapper returned a non-absolute path in watchedFiles",
        ProjectErrorKind.UnexpectedConfigIdentity => "content mapper returned configIdentity without declaring dynamicConfig",
        ProjectErrorKind.UnexpectedWatchedFiles => "content mapper returned watchedFiles without declaring dynamicConfig",
        _ => "content mapper returned an invalid project response",
    };
}

/// <summary>Go: <c>type InitializeErrorKind uint8</c>。</summary>
public enum InitializeErrorKind : byte
{
    ProcessStart = 0,
    ProcessExit = 1,
    NoResponse = 2,
    InvalidResponse = 3,
    Request = 4,
    PositionEncoding = 5,
    EmptyDiagnosticSource = 6,
    ReservedDiagnosticSource = 7,
}

/// <summary>Go: <c>type InitializeError struct</c>（mapper 的 initialize 响应不合法/不受支持）。</summary>
public sealed class InitializeError : Exception
{
    public InitializeErrorKind Kind;
    public string MapperName = "";
    public string Command = "";
    public string Detail = "";
    public int ExitCode;
    public int TimeoutSeconds;
    public PositionEncoding PositionEncoding;
    public string DiagnosticSource = "";

    public InitializeError(InitializeErrorKind kind)
        : base(MessageFor(kind, null, "", 0, default, "", 0))
    {
        Kind = kind;
    }

    // Go 的 `Error() string` 需要看全部字段，所以用一个私有构造器先算文本、再交给 base（C# 不能在 base 之前读 this 字段）。
    private InitializeError(
        InitializeErrorKind kind,
        string mapperName,
        string command,
        string detail,
        int exitCode,
        int timeoutSeconds,
        PositionEncoding positionEncoding,
        string diagnosticSource)
        : base(MessageFor(kind, command, detail, exitCode, positionEncoding, diagnosticSource, timeoutSeconds))
    {
        Kind = kind;
        MapperName = mapperName;
        Command = command;
        Detail = detail;
        ExitCode = exitCode;
        TimeoutSeconds = timeoutSeconds;
        PositionEncoding = positionEncoding;
        DiagnosticSource = diagnosticSource;
    }

    /// <summary>Go 的 <c>New*Error</c> 风格的对象初始化：设置完字段后 <see cref="Exception.Message"/> 可能过期，
    /// 所以所有构造点都走这个工厂（<c>ContentMapper.NewInitializeError(...)</c>）。</summary>
    public static InitializeError Create(
        InitializeErrorKind kind,
        string mapperName = "",
        string command = "",
        string detail = "",
        int exitCode = 0,
        int timeoutSeconds = 0,
        PositionEncoding positionEncoding = default,
        string diagnosticSource = "") =>
        new(kind, mapperName, command, detail, exitCode, timeoutSeconds, positionEncoding, diagnosticSource);

    private static string MessageFor(
        InitializeErrorKind kind,
        string? command,
        string detail,
        int exitCode,
        PositionEncoding positionEncoding,
        string diagnosticSource,
        int timeoutSeconds) => kind switch
    {
        InitializeErrorKind.ProcessStart => $"could not start content mapper command \"{command}\": {detail}",
        InitializeErrorKind.ProcessExit => $"content mapper process exited before initialization with code {exitCode}",
        InitializeErrorKind.NoResponse => "content mapper did not respond to the initialize request",
        InitializeErrorKind.InvalidResponse => "content mapper returned an invalid initialize response: " + detail,
        InitializeErrorKind.Request => "content mapper initialize request failed: " + detail,
        InitializeErrorKind.PositionEncoding => $"unsupported position encoding \"{positionEncoding.Value}\"",
        InitializeErrorKind.EmptyDiagnosticSource => "diagnostic source must not be empty",
        InitializeErrorKind.ReservedDiagnosticSource => $"diagnostic source \"{diagnosticSource}\" is reserved by TypeScript",
        _ => "content mapper initialization failed",
    };
}

/// <summary>Go: <c>type SupplementalFileCollisionError struct</c>（编译器分配的补充文件名已存在）。</summary>
public sealed class SupplementalFileCollisionError(string fileName)
    : Exception($"content mapper supplemental output file \"{fileName}\" already exists")
{
    public string FileName = fileName;
}

/// <summary>Go: <c>type Result struct</c>（把 content-mapped 源文件变换成虚拟 TypeScript 的结果）。</summary>
public sealed class Result
{
    /// <summary>Go: <c>Text</c>。被解析进程序的虚拟 TypeScript 源文本。</summary>
    public string Text = "";

    /// <summary>Go: <c>VirtualExtension</c>。决定 <see cref="Text"/> 如何解析。</summary>
    public string VirtualExtension = "";

    /// <summary>Go: <c>Diagnostics []*ast.Diagnostic</c>。原始内容里的语法错误。</summary>
    public Ast.Diagnostic[] Diagnostics = [];

    /// <summary>Go: <c>Mappings *spanmap.SpanMap</c>。把 <see cref="Text"/> 里的位置映射回原始内容；
    /// 成功的变换必须给出非 nil 映射（空映射 = 完全合成）。</summary>
    public SpanMap.SpanMap? Mappings;

    /// <summary>Go: <c>DiagnosticDirectives []ast.MappedDiagnosticDirective</c>。</summary>
    public Ast.MappedDiagnosticDirective[] DiagnosticDirectives = [];

    /// <summary>Go: <c>Supplemental []MappedResult</c>。与规范结果关联的额外无名输出。</summary>
    public MappedResult[] Supplemental = [];
}

/// <summary>Go: <c>type MappedResult struct</c>（一个虚拟源文件及其到原始输入的映射）。</summary>
public sealed class MappedResult
{
    public string Text = "";
    public string VirtualExtension = "";
    public SpanMap.SpanMap? Mappings;
    public Ast.MappedDiagnosticDirective[] DiagnosticDirectives = [];
}

/// <summary>Go: <c>type Request struct</c>（变换一个 content-mapped 源文件的输入）。</summary>
public sealed class Request
{
    /// <summary>Go: <c>FileName</c>。被变换的 content-mapped 源文件。</summary>
    public string FileName = "";

    /// <summary>Go: <c>Content</c>。该文件的文本。</summary>
    public string Content = "";
}

/// <summary>Go: <c>type ProjectSpec struct</c>（对其 content mapper 可见的项目配置）。</summary>
public sealed class ProjectSpec
{
    /// <summary>Go: <c>ConfigFileName</c>。绝对项目配置文件名；无配置文件时为空。</summary>
    public string ConfigFileName = "";

    /// <summary>Go: <c>Mappers</c>。为该项目配置的已解析 content mapper 条目。</summary>
    public Mapper[] Mappers = [];

    /// <summary>Go: <c>CompilerOptions</c>。项目生效的编译选项。</summary>
    public CompilerOptions? CompilerOptions;
}

/// <summary>Go: <c>type OptionPathSegment struct</c>（mapper 选项诊断路径的一段）。</summary>
public sealed class OptionPathSegment
{
    public string Property = "";
    public int Index;
    public bool IsIndex;
}

/// <summary>Go: <c>type OptionDiagnostic struct</c>（mapper 选项的无效之处）。</summary>
public sealed class OptionDiagnostic
{
    public Mapper? Mapper;
    public OptionPathSegment[] Path = [];
    public string Source = "";
    public int Code;
    public string MessageText = "";
}

/// <summary>Go: <c>type OperationTiming struct</c>（一种 mapper 操作的累计墙钟时间与调用次数）。</summary>
public struct OperationTiming
{
    public ulong Count;
    public TimeSpan Duration;
}

/// <summary>Go: <c>type MapperTimings struct</c>（一个 mapper identity 的累计进程/协议活动）。</summary>
public struct MapperTimings
{
    public OperationTiming Spawn;
    public OperationTiming Initialize;
    public OperationTiming OpenProject;
    public OperationTiming CloseProject;
    public OperationTiming Transform;
}

/// <summary>Go: <c>type Timings struct</c>（content mapper 进程与协议活动的累计快照）。</summary>
public sealed class Timings
{
    public Dictionary<string, MapperTimings> Mappers = new(StringComparer.Ordinal);
    public TimeSpan RequestWait;

    /// <summary>Go: <c>func (t Timings) Since(previous Timings) Timings</c>（非负增量）。</summary>
    public Timings Since(Timings previous)
    {
        var result = new Timings
        {
            Mappers = new Dictionary<string, MapperTimings>(Mappers.Count, StringComparer.Ordinal),
            RequestWait = Subtract(RequestWait, previous.RequestWait),
        };
        foreach (var (identity, current) in Mappers)
        {
            previous.Mappers.TryGetValue(identity, out var before);
            result.Mappers[identity] = new MapperTimings
            {
                Spawn = OperationTimingSince(current.Spawn, before.Spawn),
                Initialize = OperationTimingSince(current.Initialize, before.Initialize),
                OpenProject = OperationTimingSince(current.OpenProject, before.OpenProject),
                CloseProject = OperationTimingSince(current.CloseProject, before.CloseProject),
                Transform = OperationTimingSince(current.Transform, before.Transform),
            };
        }

        return result;
    }

    private static OperationTiming OperationTimingSince(OperationTiming current, OperationTiming previous) => new()
    {
        // Go: `current.Count - min(current.Count, previous.Count)`
        Count = current.Count - Math.Min(current.Count, previous.Count),
        // Go: `max(current.Duration-previous.Duration, 0)`
        Duration = Subtract(current.Duration, previous.Duration),
    };

    private static TimeSpan Subtract(TimeSpan current, TimeSpan previous)
    {
        var delta = current - previous;
        return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
    }
}

/// <summary>
/// Go: <c>type Project interface</c>。Host 的项目级视图：持有 mapper 配置句柄，提供缓存与增量构建所需的
/// identity 与 watch 依赖。mapper 项目在请求变换时惰性打开，或在需要动态配置时提前打开。
/// </summary>
public interface Project
{
    /// <summary>Go: <c>Refresh() error</c>。关闭已打开的 mapper 项目，使下次变换或 identity 查询时重新打开。</summary>
    void Refresh();

    /// <summary>Go: <c>Identities() ([]string, error)</c>。所有已配置 mapper 的变换 identity（已排序）。</summary>
    string[] Identities();

    /// <summary>Go: <c>Identity(mapper *Mapper) (string, error)</c>。mapper 的变换 identity；不在本项目里时为空串。</summary>
    string Identity(Mapper mapper);

    /// <summary>Go: <c>WatchedFiles() ([]string, error)</c>。声明了 dynamicConfig 的 mapper 报告的绝对文件。</summary>
    string[] WatchedFiles();

    /// <summary>Go: <c>Diagnostics() []OptionDiagnostic</c>。已打开的 mapper 项目缓存的选项诊断。</summary>
    OptionDiagnostic[] Diagnostics();

    /// <summary>Go: <c>Transform(mapper *Mapper, request Request) (Result, error)</c>。</summary>
    Result Transform(Mapper mapper, Request request);

    /// <summary>Go: <c>Close() error</c>。释放本项目的引用；没有引用时关闭 mapper 项目句柄。</summary>
    void Close();
}

/// <summary>
/// Go: <c>type Host interface</c>。在 program 构建期间驱动已配置的 content mapper，把不受支持的文本变成虚拟
/// TypeScript。用 <c>ContentMapper.NewHost</c> 创建；<c>Close</c> 会关闭它启动的每个 mapper 进程。
/// </summary>
public interface Host
{
    /// <summary>Go: <c>Timings() Timings</c>。mapper 进程与协议活动的累计快照。</summary>
    Timings Timings();

    /// <summary>Go: <c>Project(spec ProjectSpec) Project</c>。为 spec 返回一个持有引用的项目视图；等价 spec 共享
    /// 底层 mapper 配置状态。调用方必须关闭返回的项目。</summary>
    Project Project(ProjectSpec spec);

    /// <summary>Go: <c>Acquire(mappers []*Mapper) (release func())</c>。保留给定 mapper identity 的进程，直到
    /// 释放租约为止。获取不会启动进程（进程在 <c>Transform</c> 前保持惰性）。</summary>
    Action Acquire(Mapper[] mappers);

    /// <summary>Go: <c>SetLocale(locale locale.Locale)</c>。更新初始化 mapper 进程用的 locale；已存在的进程会被
    /// 停掉，后续变换惰性重启。</summary>
    void SetLocale(TypeScript.Locale.Locale locale);

    /// <summary>Go: <c>Transform(mapper *Mapper, request Request) (Result, error)</c>。用给定 content mapper 在
    /// 一个短命的默认选项项目里做一次变换。</summary>
    Result Transform(Mapper mapper, Request request);

    /// <summary>Go: <c>Close() error</c>。关闭 host 启动的每个 mapper 进程。</summary>
    void Close();
}
