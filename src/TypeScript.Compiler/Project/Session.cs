// ported from: internal/project/session.go:1-2120
//
// 与 Go 的偏离（逐条说明）：
//
// 1) **`Session` 内嵌 `*SnapshotHost`** ⇒ `public sealed partial class Session : SnapshotHost`
//    （partial：`internal/project/api.go` 的 `Session.APIUpdate`/`TryAdoptSnapshotInBackground` 落在
//    `Project/Api.cs`）。Go 的字段提升（`s.parseCache`/`s.newSnapshot`…）在 C# 里就是基类的
//    `_parseCache`/`NewSnapshot` 等成员。
//    Go 里 `Session.options`/`Session.toPath`/`Session.fs` **遮蔽**了内嵌 `SnapshotHost` 的同名字段
//    （Go 取"最浅的一层"）；C# 用 `internal new` 字段复刻同一解析结果（`_options`/`_toPath`/`_fs`）。
//    `Session.FS()`/`GetCurrentDirectory()`/`DefaultLibraryPath()` 同理用 `new` 隐藏基类同名方法。
//
// 2) **`atomic.Bool`**（`globalDiagPublishPending`）→ `int` + `Interlocked.CompareExchange/Exchange`
//    （`CompareAndSwap(false, true)` → `CompareExchange(ref f, 1, 0) == 0`）。
//    `sync.RWMutex`（`snapshotMu`）→ `System.Threading.Lock`（只保留互斥语义；本仓 sync.Mutex 的统一替身，
//    读不再并行——可观察行为不变）。
//
// 3) **`sync.Once`**：本文件没有；`Project`/`Snapshot` 里的 Once 见各自文件头。
//
// 4) **定时器**（Go 的 `time.AfterFunc` / `time.NewTicker`）：
//      `scheduleIdleCacheClean` 的 `time.AfterFunc(30s, f)` → `new Timer(callback, null, 30s, InfiniteTimeSpan)`
//      （一次性；`Stop()` → `Dispose()`）。回调里的 `go func() { runtime.GC() }()` → `GC.Collect()`
//      （.NET 无"后台 GC 请求"API，直接同线程触发，语义等价于"请求一次 GC"）。
//      `StartPerformanceTelemetry` 的 `time.NewTicker(5m)` → `WaitHandle.WaitOne(interval)` 循环 + 取消 token
//      （不引入 async；.NET 的 `PeriodicTimer` 需要 async 消费循环，Go 这里没有 async 路径）。
//      所有取消函数（Go 的 `context.CancelFunc`，本质就是 `func()`）都存成 `Action?` 字段。
//
// 5) **`context.WithCancel`/`WithTimeout`** → `Core.WithCancellation(ctx, cts.Token)` + 自持
//    `CancellationTokenSource`：Go 的 `cancel()` → `cts.Cancel`，Go 的 `<-ctx.Done()` →
//    `token.WaitHandle.WaitOne(delay)` / `token.IsCancellationRequested`。**不 Dispose** CTS
//    （Go 里 cancel 之后仍可再调 cancel 且无害；C# 的 Dispose 后调 Cancel 会抛）。
//
// 6) **Go runtime/system 指标无 C# 对应物**（`runtime/metrics` 的 `/gc/...`、`/memory/...`、
//    osstat 的系统内存）：`SendPerformanceTelemetry` 只填有确定对应关系的项，其余保持 0
//    （即 Go 侧"该指标不可用"的同样取值），逐项对应见方法内注释。**这不是漏移植**：Go 的 15 个
//    `gometrics.Sample` 在这套运行时里没有意义。
//
// 7) `panic("ATA module resolution should not use tracing")` → `Debug.Fail(...)`（`TypeScript.Debug`；
//    **禁止** `using System.Diagnostics;`，故 `Stopwatch`/`Process`/`UnreachableException` 走别名或全限定）。
//
// 8) `name ...any` 日志：`Logger.Logf` 用 C# 复合格式串（Go 的 `%d`/`%s`/`%v` → `{0}`/`{1}`…）。
//    Go 的 `%v` 打在切片/集合上的文本（`[a b c]`）由本文件的 `FormatGoSlice` 复刻
//    （nil → `[]`，元素以空格分隔再套方括号，与 `fmt` 对 slice 的输出一致）；`%v` 打在
//    `collections.Set` 这类"含 map 字段的结构体"上时 Go 打印 `{map[...]}`，这里改用集合元素列表
//    （两者都只是调试日志文本，**偏离已记录**）。
//
// 9) `map[K]V` → `Dictionary<K,V>`；nil 切片 → `T[]?` + `?? []`；`(T, bool)` → 本仓既定的
//    `(T Value, bool Ok)` 元组 / `TryX(out T)`；`error` 返回值 → 异常（§3.2），**除了**需要把错误对象
//    再返回给调用方（并继续使用快照）的那些点——它们保留 `(T?, Exception?)` 双返回：
//    `GetSnapshotAndDefaultProject`/`GetLanguageService`/`GetLanguageServiceAndProjectsForFile`/
//    `GetProjectsForFile`/`GetCurrentLanguageServiceWithAutoImports`/`GetLanguageServiceWithAutoImports`/
//    `WithLanguageServiceAndSnapshot`（Go 侧调用点都是 `if err != nil { … }`，且 `ErrNoProjectForUnknownScriptKind`
//    是**哨兵**，用 `error.Is` 判定属正常控制流）。`Session.ErrNoProjectForUnknownScriptKind` 即 Go 的包级哨兵。
//    `updateContentMapperRegistrations`/`updateWatches` 返回 `Exception?`（Go 返回 error，调用点只记日志）。
//
// 10) `WatcherID(fmt.Sprintf("%s.%d", w.WatcherID, i))` → `new WatcherID($"{w.WatcherID.Value}.{i}")`
//     （`WatcherID` 是手写 struct，`Value` 为原串）。`RWMutex` 下的读快照（`s.snapshot`）→ `lock (_snapshotMu)`。
//
// 11) 包级函数/哨兵：`setTristate`→`SetTristate`、`boolTelemetry`→`BoolTelemetry`、
//     `countFileStats`→`CountFileStats`、`shouldPublishProgramDiagnostics`→`Session.ShouldPublishProgramDiagnostics`
//     （都落在 `Session` 上，因为 Go 的 `project` 包不能映射成 C# 的静态类，见 Project.cs 文件头第 1 条）。
//
// 12) `ls.NewLanguageService(...)` → `Ls.LanguageService.NewLanguageService(...)`（M8Project 的引导落点）。
//     `Session : Ata.TypingsInstallerHost` 需要显式声明（Go 是结构式满足；`Ata.TypingsInstallerHost`
//     = `Ata.NpmExecutor` + `Module.ResolutionHost`，Session 的 `NpmInstall`/`FS`/`GetCurrentDirectory` 满足它）。
//     另：`NewSession(init)` 在 C# 侧**同时**提供为构造函数 `new Session(init)`（测试脚手架
//     `ProjectTestUtil` 按 `new Session(init)` 调用；`Session.NewSession(init)` 保留为 Go 同名工厂）。
//
// 13) `json.Marshal(compilerOptions)`（`map[string]any`）→ `Json.Marshal(...)` 后按 UTF-8 解码成字符串。
//
// 14) `sync.RWMutex` 的两处读快照（`s.snapshot`）→ `lock (_snapshotMu)`（只保留互斥语义）；
//     `atomic.Bool` 的 `CompareAndSwap/Store` → `Interlocked.CompareExchange/Exchange`。
//
// 15) **`Session`/`SnapshotHost` 的“同一对象”语义**（Go 的 `Session{SnapshotHost: snapshotHost}`，session.go:232）：
//     C# 不能把已存在的基类实例挂到 `this`，构造函数改为 `AdoptHost(snapshotHost)` 把宿主 8 个字段搬到 `this`，
//     并由 `this`（而不是那个临时实例）建根快照 ⇒ `snapshot._host == session`。漏搬字段会让 `Session` 自己的
//     `_contentMapperHost`/`_parseCache`/… 恒为 null：`Configure` 的 `SetLocale`（session.go:300-304）与
//     继承来的 `Close()`（session.go:1695 → `SnapshotHost.Close()`）都会静默空转。详见 SnapshotHost.cs 第 11 条。
using System.Text;
using System.Threading;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.LsUtil;
using TypeScript.Project.Logging;
using Ata = TypeScript.Project.Ata;
using Loc = TypeScript.Locale.Locale;
using Logger = TypeScript.Project.Logging.Logger;
using Path = TypeScript.TSPath.Path;
using Stopwatch = System.Diagnostics.Stopwatch;
using TelemetryEvent = TypeScript.LsProto.RequestFailureTelemetryEventOrPerformanceStatsTelemetryEventOrProjectInfoTelemetryEventOrNull;
using static TypeScript.Collections.Collections;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.LsUtil.LsUtil;
using static TypeScript.Project.Kind;
using static TypeScript.Project.Logging.Logging;
using static TypeScript.Project.ProgramUpdateKind;
using static TypeScript.Project.UpdateReason;
using static TypeScript.TSPath.TSPath;

namespace TypeScript.Project;

/// <summary>Go 的 <c>type UpdateReason int</c>（session.go:44）。成员名保留 Go 的前缀式全名，
/// 用 `using static TypeScript.Project.UpdateReason;` 在调用点裸写（与 `Kind` 的既有做法一致）。</summary>
public enum UpdateReason
{
    UpdateReasonUnknown = 0,
    UpdateReasonDidOpenFile = 1,
    UpdateReasonDidCloseFile = 2,
    UpdateReasonDidChangeCompilerOptionsForInferredProjects = 3,
    UpdateReasonRequestedLanguageServicePendingChanges = 4,
    UpdateReasonRequestedLanguageServiceProjectNotLoaded = 5,
    UpdateReasonRequestedLanguageServiceForFileNotOpen = 6,
    UpdateReasonRequestedLanguageServiceProjectDirty = 7,
    UpdateReasonRequestedLoadProjectTree = 8,
    UpdateReasonRequestedLanguageServiceWithAutoImports = 9,
    UpdateReasonIdleCleanDiskCache = 10,
    UpdateReasonDidChangeConfigFile = 11,
    UpdateReasonDidChangeContentMapperContributions = 12,
}

/// <summary>Go 的 <c>SessionOptions</c>（session.go:68-85）：会话的不可变初始化选项。
/// 字段名与 Go 逐字一致（C 组的 `ProjectCollectionBuilder` 按这些名字引用）。</summary>
public sealed class SessionOptions
{
    public string CurrentDirectory = "";

    public string DefaultLibraryPath = "";

    public string TypingsLocation = "";

    public LsProto.PositionEncodingKind PositionEncoding;

    public bool WatchEnabled;

    public bool LoggingEnabled;

    public bool TelemetryEnabled;

    public bool PushDiagnosticsEnabled;

    /// <summary>RunExternalCode allows configured content mappers to run their (external) processes,
    /// gated on workspace trust by the client. It corresponds to the --runExternalCode CLI flag.</summary>
    public bool RunExternalCode;

    /// <summary>Go: <c>DebounceDelay time.Duration</c> → <see cref="TimeSpan"/>。</summary>
    public TimeSpan DebounceDelay;

    public CheckerPoolOptions CheckerPoolOptions = new();
}

/// <summary>Go 的 <c>SessionInit</c>（session.go:87-101）。</summary>
public sealed class SessionInit
{
    public Context BackgroundCtx = Context.Background;

    public SessionOptions Options = new();

    public Vfs.FS FS = null!;

    public Client? Client;

    public Logger? Logger;

    public Ata.NpmExecutor? NpmExecutor;

    /// <summary>Spawner launches content mapper processes. It is nil when the host cannot spawn processes.</summary>
    public ContentMapper.Spawner? Spawner;

    public ContentMapper.Logger? ContentMapperLogger;

    public ParseCache? ParseCache;

    public ContentMappedParseCache? ContentMappedParseCache;
}

/// <summary>Go 的 <c>ContentMapperContributions</c>（session.go:59-62）。</summary>
public sealed class ContentMapperContributions
{
    public ContentMapper.Mapper[] Mappers = [];

    public string[] Extensions = [];
}

/// <summary>Go 的 <c>Session</c>（session.go:104-172）：LSP 会话状态机，把事件过程化成不可变快照。</summary>
public sealed partial class Session : SnapshotHost, Ata.TypingsInstallerHost
{
    /// <summary>Go 的包级哨兵 <c>ErrNoProjectForUnknownScriptKind</c>（session.go:46）。</summary>
    public static readonly Exception ErrNoProjectForUnknownScriptKind = new("no project for unknown script kind");

    /// <summary>Go: <c>watchRequestTimeout</c>（session.go:64）。</summary>
    private static readonly TimeSpan _watchRequestTimeout = TimeSpan.FromSeconds(1);

    /// <summary>Go: <c>idleCacheCleanDelay</c>（session.go:585）。</summary>
    private static readonly TimeSpan _idleCacheCleanDelay = TimeSpan.FromSeconds(30);

    /// <summary>Go: <c>performanceTelemetryInterval</c>（session.go:709）。</summary>
    private static readonly TimeSpan _performanceTelemetryInterval = TimeSpan.FromMinutes(5);

    /// <summary>Go: <c>SnapshotHost</c>（内嵌）+ 自身的 <c>options</c>（见文件头第 1 条）。</summary>
    internal new SessionOptions _options = null!;

    internal Logger _logger = null!;

    private Context _backgroundCtx = null!;

    internal new Func<string, Path> _toPath = null!;

    internal Client? _client;

    private readonly long _startTime;

    internal Ata.NpmExecutor? _npmExecutor;

    internal new OverlayFS _fs = null!;

    /// <summary>contentMapperTimings is the cumulative host snapshot at the most recent session snapshot adoption.</summary>
    internal ContentMapper.Timings _contentMapperTimings = new();
    internal readonly Lock _contentMapperTimingsMu = new();

    /// <summary>registeredContentMapperSnapshotID is the ID of the newest snapshot whose registration has been
    /// applied. Registration runs from background tasks that may finish out of order, so
    /// contentMapperRegistrationMu serializes updates and the snapshot ID keeps a stale task from
    /// overwriting a newer snapshot's registration.</summary>
    internal string[] _registeredContentMapperExtensions = [];
    internal ulong _registeredContentMapperSnapshotID;
    internal readonly Lock _contentMapperRegistrationMu = new();

    // read-only after initialization
    internal UserPreferences _initialUserPreferences;

    // current preferences
    internal UserPreferences _workspaceUserPreferences;

    internal CompilerOptions? _compilerOptionsForInferredProjects;

    internal Ata.TypingsInstaller? _typingsInstaller;

    internal Background.Queue _backgroundQueue = null!;

    /// <summary>snapshot is the current immutable state of all projects.</summary>
    internal Snapshot _snapshot = null!;
    internal readonly Lock _snapshotMu = new();
    internal readonly Lock _snapshotUpdateMu = new();

    /// <summary>scheduledSnapshotUpdateCancel is the cancelation function for a scheduled
    /// snapshot update. Snapshot updates are scheduled and debounced after file closes.</summary>
    private Action? _scheduledSnapshotUpdateCancel;
    private ulong _scheduledSnapshotUpdateGeneration;
    internal readonly Lock _scheduledSnapshotUpdateMu = new();

    internal bool _pendingUserConfigChanges;
    internal readonly Lock _configureMu = new();
    internal readonly Lock _userConfigRWMu = new();

    /// <summary>pendingFileChanges are accumulated from textDocument/* events delivered
    /// by the LSP server through DidOpenFile(), DidChangeFile(), etc. They are
    /// applied to the next snapshot update.</summary>
    internal List<FileChange> _pendingFileChanges = [];
    internal readonly Lock _pendingFileChangesMu = new();

    /// <summary>pendingATAChanges are produced by Automatic Type Acquisition (ATA)
    /// installations and applied to the next snapshot update.</summary>
    internal Dictionary<ID, ATAStateChange> _pendingATAChanges = [];
    internal readonly Lock _pendingATAChangesMu = new();

    /// <summary>diagnosticsRefreshCancel is the cancelation function for a scheduled
    /// diagnostics refresh. Diagnostics refreshes are scheduled and debounced
    /// after file watch changes and ATA updates.</summary>
    private Action? _diagnosticsRefreshCancel;
    private ulong _diagnosticsRefreshGeneration;
    internal readonly Lock _diagnosticsRefreshMu = new();

    /// <summary>warmAutoImportCancel is the cancelation function for a running
    /// auto-import cache warming task. It is cancelled on file opens,
    /// closes, changes, watched-file changes, new auto-import warming
    /// requests, and when the session closes.</summary>
    private Action? _warmAutoImportCancel;
    internal readonly Lock _warmAutoImportMu = new();

    /// <summary>idleCacheCleanTimer is a resettable timer for scheduling idle disk
    /// cache cleans. The timer resets on any file event (open, close,
    /// change, save, watch) and fires after 30 seconds of inactivity.</summary>
    private Timer? _idleCacheCleanTimer;
    internal readonly Lock _idleCacheCleanMu = new();

    /// <summary>performanceTelemetryCancel cancels the periodic performance telemetry ticker.</summary>
    private Action? _performanceTelemetryCancel;

    /// <summary>seenProjects tracks projects that have already had telemetry sent.</summary>
    internal SyncSet<ID> _seenProjects = new();

    /// <summary>watches tracks the current watch globs and how many individual WatchedFiles
    /// are using each glob.</summary>
    internal WatchRegistry? _watches;

    /// <summary>globalDiagPublishPending is set to true when a global diagnostics publish
    /// task should be enqueued. It is reset when the task runs, coalescing multiple
    /// requests into a single background task（见文件头第 2 条）。</summary>
    private int _globalDiagPublishPending;

    /// <summary>Go: <c>func NewSession(init *SessionInit) *Session</c>（session.go:204-252）。
    /// C# 侧同时提供构造函数（测试脚手架用 `new Session(init)`）与 Go 同名的静态工厂
    /// <see cref="NewSession(SessionInit)"/>。</summary>
    public Session(SessionInit init)
    {
        var snapshotHost = NewSnapshotHost(init);
        var sessionLogger = init.Logger ?? NewNopLogger();
        // Go 的 `Session{SnapshotHost: snapshotHost}`（同一对象）→ 把宿主字段搬到 this，
        // 之后 `_snapshot` 也由 this 建（`snapshot._host` 与 `session` 是同一个对象，与 Go 一致）。
        AdoptHost(snapshotHost);
        _options = init.Options;
        _logger = sessionLogger;
        _backgroundCtx = init.BackgroundCtx;
        _toPath = snapshotHost._toPath;
        _client = init.Client;
        _npmExecutor = init.NpmExecutor;
        _fs = OverlayFS.NewOverlayFS(snapshotHost._fs, [], init.Options.PositionEncoding, snapshotHost._toPath);
        _backgroundQueue = Background.Queue.NewQueue();
        _startTime = Stopwatch.GetTimestamp();
        _snapshot = NewRootSnapshot(
            0,
            LsProto.LsProto.GetClientCapabilities().Workspace.DidChangeWatchedFiles.RelativePatternSupport);
        _initialUserPreferences = NewDefaultUserPreferences();
        _workspaceUserPreferences = NewDefaultUserPreferences();
        _pendingATAChanges = [];
        _watches = WatchRegistry.NewWatchRegistry();

        if (init.Options.TypingsLocation != "" && init.NpmExecutor is not null)
        {
            _typingsInstaller = Ata.TypingsInstaller.NewTypingsInstaller(
                new Ata.TypingsInstallerOptions
                {
                    TypingsLocation = init.Options.TypingsLocation,
                    ThrottleLimit = 5,
                },
                this);
        }

        if (snapshotHost._contentMapperHost is not null)
        {
            _contentMapperTimings = snapshotHost._contentMapperHost.Timings();
        }
    }

    /// <summary>Go 的同名工厂（见构造函数）。</summary>
    public static Session NewSession(SessionInit init)
    {
        return new Session(init);
    }

    /// <summary>FS implements module.ResolutionHost（session.go:255-258）。</summary>
    public new Vfs.FS FS()
    {
        return _fs;
    }

    /// <summary>GetCurrentDirectory implements module.ResolutionHost（session.go:261-263）。</summary>
    public new string GetCurrentDirectory()
    {
        return _options.CurrentDirectory;
    }

    public new string DefaultLibraryPath()
    {
        return _options.DefaultLibraryPath;
    }

    /// <summary>Gets copy of current configuration（session.go:270-275）。</summary>
    public UserPreferences Config()
    {
        lock (_userConfigRWMu)
        {
            return _workspaceUserPreferences;
        }
    }

    private Context BackgroundContext()
    {
        return WithCurrentLocale(_backgroundCtx);
    }

    public Context WithCurrentLocale(Context ctx)
    {
        if (_client is null)
        {
            return ctx;
        }

        return Loc.WithLocale(ctx, _client.GetLocale());
    }

    /// <summary>Trace implements module.ResolutionHost（session.go:285-288）。</summary>
    public void Trace(string msg)
    {
        // Go: panic("ATA module resolution should not use tracing")
        Debug.Fail("ATA module resolution should not use tracing");
    }

    /// <summary>Go: <c>func (s *Session) Configure(config lsutil.UserPreferences)</c>（session.go:290-315）。</summary>
    public void Configure(UserPreferences config)
    {
        lock (_configureMu)
        {
            UserPreferences oldConfig;
            lock (_userConfigRWMu)
            {
                _pendingUserConfigChanges = true;
                oldConfig = _workspaceUserPreferences;
                _workspaceUserPreferences = config;
            }

            if (config.Locale != "")
            {
                var oldLocale = _client!.GetLocale();
                _client.SetLocale(config.Locale);
                var newLocale = _client.GetLocale();
                if (oldLocale.String() != newLocale.String())
                {
                    _contentMapperHost?.SetLocale(newLocale);
                }
            }

            // Tell the client to re-request certain commands depending on user preference changes.
            RefreshInlayHintsIfNeeded(oldConfig, config);
            RefreshCodeLensIfNeeded(oldConfig, config);
            RefreshDiagnosticsIfNeeded(oldConfig, config);
            RefreshATAIfNeeded(oldConfig, config);
        }
    }

    public void InitializeWithUserConfig(UserPreferences config)
    {
        _initialUserPreferences = config;
        Configure(config);
    }

    /// <summary>Go: <c>func (s *Session) DidOpenFile(ctx, uri, version, content, languageKind)</c>（session.go:325-349）。</summary>
    public void DidOpenFile(Context ctx, LsProto.DocumentUri uri, int version, string content, LsProto.LanguageKind languageKind)
    {
        CancelWarmAutoImportCache();
        ScheduleIdleCacheClean();
        CancelScheduledSnapshotUpdate();
        lock (_snapshotUpdateMu)
        {
            FileChangeSummary changes;
            Dictionary<Path, Overlay> overlays;
            lock (_pendingFileChangesMu)
            {
                _pendingFileChanges.Add(new FileChange
                {
                    Kind = FileChangeKind.Open,
                    URI = uri,
                    Version = version,
                    Content = content,
                    LanguageKind = languageKind,
                });
                (changes, overlays) = FlushChangesLocked(ctx);
            }

            UpdateSnapshot(ctx, overlays, new SnapshotChange
            {
                _reason = UpdateReasonDidOpenFile,
                _fileChanges = changes,
                Documents = [uri],
            });
        }
    }

    /// <summary>SetContentMapperContributions atomically replaces extension-provided inferred-project mappers and
    /// discovers configured projects for matching open documents. Configured projects never consume these mappers
    /// （session.go:351-368）。</summary>
    public void SetContentMapperContributions(Context ctx, ContentMapperContributions contributions, LsProto.DocumentUri[] documentURIs)
    {
        if (!_options.RunExternalCode)
        {
            return;
        }

        CancelScheduledSnapshotUpdate();
        lock (_snapshotUpdateMu)
        {
            FileChangeSummary changes;
            Dictionary<Path, Overlay> overlays;
            lock (_pendingFileChangesMu)
            {
                (changes, overlays) = FlushChangesLocked(ctx);
            }

            UpdateSnapshot(ctx, overlays, new SnapshotChange
            {
                _reason = UpdateReasonDidChangeContentMapperContributions,
                _fileChanges = changes,
                _contentMapperContributions = contributions,
                ConfiguredProjectDocuments = documentURIs,
            });
        }

        UpdateContentMapperRegistrations(ctx, Snapshot());
    }

    /// <summary>Go: <c>func (s *Session) DidCloseFile(ctx, uri)</c>（session.go:370-381）。</summary>
    public void DidCloseFile(Context ctx, LsProto.DocumentUri uri)
    {
        CancelWarmAutoImportCache();
        ScheduleIdleCacheClean();
        lock (_pendingFileChangesMu)
        {
            _pendingFileChanges.Add(new FileChange
            {
                Kind = FileChangeKind.Close,
                URI = uri,
            });
        }

        ScheduleSnapshotUpdate(UpdateReasonDidCloseFile);
    }

    /// <summary>Go: <c>func (s *Session) DidChangeFile(ctx, uri, version, changes)</c>（session.go:383-410）。</summary>
    public void DidChangeFile(Context ctx, LsProto.DocumentUri uri, int version, LsProto.TextDocumentContentChangePartialOrWholeDocument[] changes)
    {
        CancelWarmAutoImportCache();
        ScheduleIdleCacheClean();
        lock (_pendingFileChangesMu)
        {
            _pendingFileChanges.Add(new FileChange
            {
                Kind = FileChangeKind.Change,
                URI = uri,
                Version = version,
                Changes = changes,
            });
        }

        // Editing a content-mapped file changes the program like any source edit, but the client's
        // pull-diagnostics machinery won't re-request diagnostics for dependent files: the content-mapped file is not
        // in the diagnostic provider's document selector, so a change to it never triggers the client's
        // inter-file re-pull. Prompt a workspace refresh so dependents update. We skip the debounce here so the
        // edit doesn't feel sluggish (normal source edits are pulled per-keystroke client-side); the refresh is
        // still coalesced. Ordinary source files are handled entirely client-side, so we cancel any pending
        // refresh for them as before.
        if (IsContentMapperFile(uri))
        {
            ScheduleDiagnosticsRefresh(TimeSpan.Zero);
        }
        else
        {
            CancelDiagnosticsRefresh();
        }
    }

    /// <summary>isContentMapperFile reports whether uri is a content-mapped file handled by a configured content mapper, based
    /// on the extensions currently registered with the client for text document synchronization（session.go:412-418）。</summary>
    private bool IsContentMapperFile(LsProto.DocumentUri uri)
    {
        var snapshot = Snapshot();
        var configured = snapshot.ConfigFileRegistry.ContentMappers();
        var extensions = new List<string>(configured._extensions);
        extensions.AddRange(snapshot._inferredProjectContentMapperExtensions);
        return FileExtensionIsOneOf(uri.FileName(), [.. extensions]);
    }

    /// <summary>Go: <c>func (s *Session) DidSaveFile(ctx, uri)</c>（session.go:420-428）。</summary>
    public void DidSaveFile(Context ctx, LsProto.DocumentUri uri)
    {
        ScheduleIdleCacheClean();
        lock (_pendingFileChangesMu)
        {
            _pendingFileChanges.Add(new FileChange
            {
                Kind = FileChangeKind.Save,
                URI = uri,
            });
        }
    }

    /// <summary>Go: <c>func (s *Session) DidChangeWatchedFiles(ctx, changes)</c>（session.go:430-497）。</summary>
    public void DidChangeWatchedFiles(Context ctx, LsProto.FileEvent[] changes)
    {
        var fileChanges = new List<FileChange>(changes.Length);
        var hasRelevantChange = false;
        var hasConfigChange = false;
        var snapshot = Snapshot();
        var configFileRegistry = snapshot.ConfigFileRegistry;
        var (contentMapperExtensions, contentMapperWatchedFiles) = snapshot.ContentMapperWatchState();
        foreach (var change in changes)
        {
            FileChangeKind kind;
            switch (change.Type)
            {
                case LsProto.FileChangeType.Created:
                    kind = FileChangeKind.WatchCreate;
                    break;
                case LsProto.FileChangeType.Changed:
                    kind = FileChangeKind.WatchChange;
                    break;
                case LsProto.FileChangeType.Deleted:
                    kind = FileChangeKind.WatchDelete;
                    break;
                default:
                    continue; // Ignore unknown change types.
            }

            fileChanges.Add(new FileChange
            {
                Kind = kind,
                URI = change.Uri,
            });

            if (!hasConfigChange && configFileRegistry.IsTracked(_toPath(change.Uri.FileName())))
            {
                hasConfigChange = true;
            }

            if (!hasRelevantChange)
            {
                var fileName = change.Uri.FileName();
                var path = _toPath(fileName).RemoveTrailingDirectorySeparator();
                var pathStr = path.Value;
                if (contentMapperWatchedFiles!.Has(path))
                {
                    hasRelevantChange = true;
                    continue;
                }

                var i = pathStr.LastIndexOf('.');
                if (i < 0 || pathStr.LastIndexOf('/') > i)
                {
                    // Extensionless paths might be directories.
                    // For creations/changes, we can check the file system.
                    // For deletions, consult the current snapshot cache to avoid treating extensionless file deletions as relevant.
                    if (kind != FileChangeKind.WatchDelete)
                    {
                        hasRelevantChange = _fs.DirectoryExists(fileName);
                    }
                    else
                    {
                        Snapshot currentSnapshot;
                        lock (_snapshotMu)
                        {
                            currentSnapshot = _snapshot;
                        }

                        if (currentSnapshot._fs._cacheDirectories?.ContainsKey(path) == true || currentSnapshot.HasOverlayWithin(path) || SnapshotFSBuilder.IsNodeModulesPath(path))
                        {
                            hasRelevantChange = true;
                        }
                    }
                }
                else
                {
                    if (SnapshotFSBuilder.IsRelevantExtension(pathStr[i..]) ||
                        FileExtensionIsOneOf(pathStr, contentMapperExtensions))
                    {
                        hasRelevantChange = true;
                    }
                }
            }
        }

        lock (_pendingFileChangesMu)
        {
            _pendingFileChanges.AddRange(fileChanges);
        }

        if (hasRelevantChange)
        {
            // Schedule a debounced diagnostics refresh only for paths
            // that can affect the TypeScript program (relevant extensions or directories).
            ScheduleDiagnosticsRefresh();
        }

        if (hasConfigChange)
        {
            // Config file diagnostics are pushed on snapshot updates rather than pulled,
            // so they must not depend on the client re-pulling diagnostics in response to
            // the refresh request above.
            ScheduleSnapshotUpdate(UpdateReasonDidChangeConfigFile);
        }

        CancelWarmAutoImportCache();
        ScheduleIdleCacheClean();
    }

    /// <summary>Go: <c>func (s *Session) DidChangeCompilerOptionsForInferredProjects(ctx, options)</c>（session.go:499-505）。</summary>
    public void DidChangeCompilerOptionsForInferredProjects(Context ctx, CompilerOptions options)
    {
        _compilerOptionsForInferredProjects = options;
        UpdateSnapshot(ctx, _fs.Overlays(), new SnapshotChange
        {
            _reason = UpdateReasonDidChangeCompilerOptionsForInferredProjects,
            _compilerOptionsForInferredProjects = options,
        });
    }

    public void ScheduleDiagnosticsRefresh()
    {
        ScheduleDiagnosticsRefresh(_options.DebounceDelay);
    }

    /// <summary>scheduleDiagnosticsRefresh schedules a coalesced workspace diagnostics refresh after delay. A delay of
    /// 0 refreshes as soon as the background queue runs the task; it is used for interactive edits (e.g. a
    /// content-mapped file) where the debounce would make dependent-file diagnostics feel sluggish
    /// （session.go:512-570）。</summary>
    private void ScheduleDiagnosticsRefresh(TimeSpan delay)
    {
        lock (_diagnosticsRefreshMu)
        {
            // Cancel any existing scheduled diagnostics refresh
            if (_diagnosticsRefreshCancel is not null)
            {
                _diagnosticsRefreshCancel();
                _logger.Log("Delaying scheduled diagnostics refresh...");
            }
            else
            {
                _logger.Log("Scheduling new diagnostics refresh...");
            }

            // Create a new cancellable context for the debounce task
            var cts = new CancellationTokenSource();
            var debounceCtx = WithCancellation(BackgroundContext(), cts.Token);
            _diagnosticsRefreshGeneration++;
            var generation = _diagnosticsRefreshGeneration;
            _diagnosticsRefreshCancel = cts.Cancel;

            // Enqueue the (optionally debounced) diagnostics refresh
            _backgroundQueue.Enqueue(debounceCtx, ctx =>
            {
                try
                {
                    if (delay > TimeSpan.Zero)
                    {
                        // Wait out the debounce window; a newer event cancels this one.
                        if (ctx.CancellationToken.WaitHandle.WaitOne(delay))
                        {
                            // Context was cancelled, newer events arrived
                            return;
                        }
                    }
                    else if (ctx.CancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    // Clear the cancel function since we're about to execute the refresh
                    lock (_diagnosticsRefreshMu)
                    {
                        if (_diagnosticsRefreshGeneration != generation)
                        {
                            return;
                        }

                        _diagnosticsRefreshCancel = null;
                    }

                    if (_options.LoggingEnabled)
                    {
                        _logger.Log("Running scheduled diagnostics refresh");
                    }

                    try
                    {
                        _client!.RefreshDiagnostics(BackgroundContext());
                    }
                    catch (Exception e)
                    {
                        if (_options.LoggingEnabled)
                        {
                            _logger.Logf("Error refreshing diagnostics: {0}", e.Message);
                        }
                    }
                }
                finally
                {
                    cts.Cancel();
                }
            });
        }
    }

    private void CancelDiagnosticsRefresh()
    {
        lock (_diagnosticsRefreshMu)
        {
            if (_diagnosticsRefreshCancel is not null)
            {
                _diagnosticsRefreshCancel();
                _logger.Log("Canceled scheduled diagnostics refresh");
                _diagnosticsRefreshCancel = null;
                _diagnosticsRefreshGeneration++;
            }
        }
    }

    /// <summary>Go: <c>func (s *Session) ScheduleSnapshotUpdate(reason UpdateReason)</c>（session.go:572-630）。</summary>
    public void ScheduleSnapshotUpdate(UpdateReason reason)
    {
        lock (_scheduledSnapshotUpdateMu)
        {
            // Cancel any existing scheduled snapshot update
            if (_scheduledSnapshotUpdateCancel is not null)
            {
                _scheduledSnapshotUpdateCancel();
                if (_options.LoggingEnabled)
                {
                    _logger.Log("Delaying scheduled snapshot update...");
                }
            }
            else if (_options.LoggingEnabled)
            {
                _logger.Log("Scheduling new snapshot update...");
            }

            // Create a new cancellable context for the debounce task
            var cts = new CancellationTokenSource();
            var debounceCtx = WithCancellation(BackgroundContext(), cts.Token);
            _scheduledSnapshotUpdateGeneration++;
            var generation = _scheduledSnapshotUpdateGeneration;
            _scheduledSnapshotUpdateCancel = cts.Cancel;

            // Enqueue the debounced snapshot update
            _backgroundQueue.Enqueue(debounceCtx, ctx =>
            {
                try
                {
                    // Sleep for the debounce delay
                    if (ctx.CancellationToken.WaitHandle.WaitOne(_options.DebounceDelay))
                    {
                        // Context was cancelled, newer events arrived or another snapshot update ran
                        return;
                    }

                    // Clear the cancel function since we're about to execute the update
                    lock (_scheduledSnapshotUpdateMu)
                    {
                        if (_scheduledSnapshotUpdateGeneration != generation)
                        {
                            return;
                        }

                        _scheduledSnapshotUpdateCancel = null;
                    }

                    if (_options.LoggingEnabled)
                    {
                        _logger.Log("Running scheduled snapshot update");
                    }

                    lock (_snapshotUpdateMu)
                    {
                        var (fileChanges, overlays, ataChanges, newConfig) = FlushChanges(ctx);
                        if (fileChanges.IsEmpty() && ataChanges.Count == 0 && newConfig is null)
                        {
                            return;
                        }

                        UpdateSnapshot(ctx, overlays, new SnapshotChange
                        {
                            _reason = reason,
                            _fileChanges = fileChanges,
                            _ataChanges = ataChanges,
                            _newConfig = newConfig,
                        });
                    }
                }
                finally
                {
                    cts.Cancel();
                }
            });
        }
    }

    private void CancelScheduledSnapshotUpdate()
    {
        lock (_scheduledSnapshotUpdateMu)
        {
            if (_scheduledSnapshotUpdateCancel is not null)
            {
                _scheduledSnapshotUpdateCancel();
                if (_options.LoggingEnabled)
                {
                    _logger.Log("Canceled scheduled snapshot update");
                }

                _scheduledSnapshotUpdateCancel = null;
                _scheduledSnapshotUpdateGeneration++;
            }
        }
    }

    private void CancelWarmAutoImportCache()
    {
        lock (_warmAutoImportMu)
        {
            if (_warmAutoImportCancel is not null)
            {
                _warmAutoImportCancel();
                _warmAutoImportCancel = null;
            }
        }
    }

    /// <summary>Go: <c>func (s *Session) scheduleIdleCacheClean()</c>（session.go:587-621，定时器见文件头第 4 条）。</summary>
    private void ScheduleIdleCacheClean()
    {
        lock (_idleCacheCleanMu)
        {
            _idleCacheCleanTimer?.Dispose();

            _idleCacheCleanTimer = new Timer(
                _ =>
                {
                    lock (_idleCacheCleanMu)
                    {
                        _idleCacheCleanTimer = null;
                    }

                    lock (_snapshotUpdateMu)
                    {
                        CancelScheduledSnapshotUpdate();

                        var ctx = BackgroundContext();
                        var (fileChanges, overlays, ataChanges, newConfig) = FlushChanges(ctx);
                        UpdateSnapshot(ctx, overlays, new SnapshotChange
                        {
                            _reason = UpdateReasonIdleCleanDiskCache,
                            _fileChanges = fileChanges,
                            _ataChanges = ataChanges,
                            _newConfig = newConfig,
                            _cleanFileCache = true,
                        });
                    }

                    // Go: go func() { runtime.GC() }()
                    GC.Collect();
                },
                null,
                _idleCacheCleanDelay,
                Timeout.InfiniteTimeSpan);
        }
    }

    private void CancelIdleCacheClean()
    {
        lock (_idleCacheCleanMu)
        {
            if (_idleCacheCleanTimer is not null)
            {
                _idleCacheCleanTimer.Dispose();
                _idleCacheCleanTimer = null;
            }
        }
    }

    /// <summary>StartPerformanceTelemetry begins periodic collection and sending of performance
    /// telemetry. It should be called once after the session is initialized（session.go:715-739）。</summary>
    public void StartPerformanceTelemetry()
    {
        if (!_options.TelemetryEnabled)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        var ctx = WithCancellation(BackgroundContext(), cts.Token);
        _performanceTelemetryCancel = cts.Cancel;
        _backgroundQueue.Enqueue(ctx, tickCtx =>
        {
            // Go: time.NewTicker(performanceTelemetryInterval) + select{ <-ctx.Done() / <-ticker.C }
            while (!tickCtx.CancellationToken.WaitHandle.WaitOne(_performanceTelemetryInterval))
            {
                if (_client is null || !_client.IsActive())
                {
                    continue;
                }

                SendPerformanceTelemetry(tickCtx);
            }
        });
    }

    private void StopPerformanceTelemetry()
    {
        if (_performanceTelemetryCancel is not null)
        {
            _performanceTelemetryCancel();
            _performanceTelemetryCancel = null;
        }
    }

    /// <summary>Go: <c>func (s *Session) sendPerformanceTelemetry(ctx)</c>（session.go:741-853，指标对应见文件头第 6 条）。</summary>
    internal void SendPerformanceTelemetry(Context ctx)
    {
        if (_client is null || !_options.TelemetryEnabled)
        {
            return;
        }

        Snapshot snapshot;
        lock (_snapshotMu)
        {
            snapshot = _snapshot;
        }

        var measurements = new LsProto.PerformanceStatsTelemetryMeasurements
        {
            OpenFileCount = snapshot.Overlays().Count,
            UptimeSeconds = Stopwatch.GetElapsedTime(_startTime).TotalSeconds,
            ProjectCount = snapshot.ProjectCollection.Projects().Length,
            ConfigCount = snapshot.ConfigFileRegistry._configs.Count,
            CachedDiskFileCount = snapshot._fs._cacheFiles?.Count ?? 0,
        };

        // Go 读 runtime/metrics（/memory、/gc、/sched、/cpu）+ osstat 的系统内存。.NET 里：
        //   MemoryUsedBytes  ← GC.GetTotalMemory(false)（已分配托管字节）
        //   HeapLiveBytes    ← GCMemoryInfo.HeapSizeBytes（活对象字节）
        //   GoMemLimit       ← GCMemoryInfo.TotalAvailableMemoryBytes（GOMEMLIMIT 的对应物）
        //   GoMaxProcs       ← Environment.ProcessorCount（GOMAXPROCS 的对应物）
        //   GcCyclesTotal    ← 三代 GC 触发次数之和
        //   UserCPUSeconds   ← 进程用户态 CPU 秒数
        //   SystemMemTotal/SystemMemUsed ← 可用内存 / 进程工作集（**不是**系统级"已用内存"）
        // 其余（GoGCPercent/HeapGoalBytes/HeapObjectCount/HeapStackBytes/HeapReleasedBytes/HeapFreeBytes/
        // GcScanHeapBytes/GoroutineCount/GcCPUSeconds）在 .NET 无对应物，保持 0。
        var gcInfo = GC.GetGCMemoryInfo();
        var process = System.Diagnostics.Process.GetCurrentProcess();
        measurements.MemoryUsedBytes = GC.GetTotalMemory(false);
        measurements.GoMemLimit = gcInfo.TotalAvailableMemoryBytes;
        measurements.HeapLiveBytes = gcInfo.HeapSizeBytes;
        measurements.GoMaxProcs = Environment.ProcessorCount;
        measurements.GcCyclesTotal = GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2);
        measurements.UserCPUSeconds = process.TotalProcessorTime.TotalSeconds;
        measurements.SystemMemTotal = gcInfo.TotalAvailableMemoryBytes;
        measurements.SystemMemUsed = process.WorkingSet64;

        // Read auto-import registry stats
        if (snapshot.AutoImportRegistry() is { } registry)
        {
            var autoImportStats = registry.GetCacheStats();
            measurements.AutoImportProjectBucketCount = autoImportStats.ProjectBuckets.Count;
            measurements.AutoImportNodeModulesBucketCount = autoImportStats.NodeModulesBuckets.Count;
            measurements.AutoImportUniquePackageCount = autoImportStats.UniquePackageCount;
            foreach (var b in autoImportStats.ProjectBuckets)
            {
                measurements.AutoImportProjectExportCount += b.ExportCount;
                measurements.AutoImportProjectFileCount += b.FileCount;
            }

            foreach (var b in autoImportStats.NodeModulesBuckets)
            {
                measurements.AutoImportNodeModulesExportCount += b.ExportCount;
                measurements.AutoImportNodeModulesFileCount += b.FileCount;
                if (b.DependencyNames is null)
                {
                    measurements.AutoImportNodeModulesUnfilteredBucketCount++;
                }
            }
        }

        try
        {
            _client.SendTelemetry(ctx, new TelemetryEvent
            {
                PerformanceStatsTelemetryEvent = new LsProto.PerformanceStatsTelemetryEvent
                {
                    Measurements = measurements,
                },
            });
        }
        catch (Exception e)
        {
            if (_options.LoggingEnabled)
            {
                _logger.Logf("Error sending performance telemetry: {0}", e.Message);
            }
        }
    }

    internal void SendProjectInfoTelemetryForNewProjects(Snapshot oldSnapshot, Snapshot newSnapshot)
    {
        if (!_options.TelemetryEnabled)
        {
            return;
        }

        var ctx = BackgroundContext();
        DiffOrderedMaps(
            oldSnapshot.ProjectCollection.ProjectsByID(),
            newSnapshot.ProjectCollection.ProjectsByID(),
            (_, addedProject) => SendProjectInfoTelemetry(ctx, addedProject!),
            (_, _) => { },
            (_, _, _) => { });
    }

    private void SendProjectInfoTelemetry(Context ctx, Project project)
    {
        if (_client is null || !_options.TelemetryEnabled)
        {
            return;
        }

        if (_seenProjects.Has(project.ID()))
        {
            return;
        }

        if (project.Program is null || project.CommandLine is null)
        {
            return;
        }

        var info = CollectProjectInfoTelemetry(project);
        try
        {
            _client.SendTelemetry(ctx, info);
        }
        catch (Exception e)
        {
            if (_options.LoggingEnabled)
            {
                _logger.Logf("Error sending project info telemetry: {0}", e.Message);
            }

            return;
        }

        _seenProjects.Add(project.ID());
    }

    /// <summary>Go: <c>func (s *Session) collectProjectInfoTelemetry(project *Project) lsproto.TelemetryEvent</c>
    /// （session.go:889-968）。</summary>
    private TelemetryEvent CollectProjectInfoTelemetry(Project project)
    {
        var opts = project.CommandLine!.CompilerOptions() ?? new CompilerOptions();

        var configFileName = "other";
        if (project.Kind == KindConfigured)
        {
            var baseName = GetBaseFileName(project.ConfigFileName());
            if (baseName == "tsconfig.json" || baseName == "jsconfig.json")
            {
                configFileName = baseName;
            }
        }

        var projectType = "inferred";
        if (project.Kind == KindConfigured)
        {
            projectType = "configured";
        }

        var props = new Dictionary<string, string>
        {
            ["configFileName"] = configFileName,
            ["projectType"] = projectType,
            ["version"] = Version(),
        };

        // Compiler options — same approach as Strada's convertCompilerOptionsForTelemetry:
        // booleans and enum string names, no paths.
        var compilerOptions = new Dictionary<string, object?>();
        SetTristate(compilerOptions, "strict", opts.Strict);
        SetTristate(compilerOptions, "noImplicitAny", opts.NoImplicitAny);
        SetTristate(compilerOptions, "noImplicitThis", opts.NoImplicitThis);
        SetTristate(compilerOptions, "strictNullChecks", opts.StrictNullChecks);
        SetTristate(compilerOptions, "strictFunctionTypes", opts.StrictFunctionTypes);
        SetTristate(compilerOptions, "strictBindCallApply", opts.StrictBindCallApply);
        SetTristate(compilerOptions, "strictPropertyInitialization", opts.StrictPropertyInitialization);
        SetTristate(compilerOptions, "strictBuiltinIteratorReturn", opts.StrictBuiltinIteratorReturn);
        SetTristate(compilerOptions, "useUnknownInCatchVariables", opts.UseUnknownInCatchVariables);
        SetTristate(compilerOptions, "exactOptionalPropertyTypes", opts.ExactOptionalPropertyTypes);
        SetTristate(compilerOptions, "allowJs", opts.AllowJs);
        SetTristate(compilerOptions, "checkJs", opts.CheckJs);
        SetTristate(compilerOptions, "noEmit", opts.NoEmit);
        SetTristate(compilerOptions, "declaration", opts.Declaration);
        SetTristate(compilerOptions, "composite", opts.Composite);
        SetTristate(compilerOptions, "isolatedModules", opts.IsolatedModules);
        SetTristate(compilerOptions, "skipLibCheck", opts.SkipLibCheck);
        SetTristate(compilerOptions, "incremental", opts.Incremental);
        if (opts.Target != ScriptTarget.None)
        {
            compilerOptions["target"] = opts.Target.String();
        }

        if (opts.Module != ModuleKind.None)
        {
            compilerOptions["module"] = opts.Module.String();
        }

        if (opts.ModuleResolution != ModuleResolutionKind.Unknown)
        {
            compilerOptions["moduleResolution"] = opts.ModuleResolution.String();
        }

        if (opts.Jsx != JsxEmit.None)
        {
            compilerOptions["jsx"] = opts.Jsx.String();
        }

        try
        {
            props["compilerOptions"] = Encoding.UTF8.GetString(Json.Json.Marshal(compilerOptions));
        }
        catch (Exception)
        {
            // Go: `if b, err := json.Marshal(compilerOptions); err == nil { … }`——失败时不写这个属性。
        }

        // Config file shape
        if (project.CommandLine.Raw is OrderedMap<string, object?> raw)
        {
            props["extends"] = BoolTelemetry(raw.Has("extends"));
            props["files"] = BoolTelemetry(raw.Has("files"));
            props["include"] = BoolTelemetry(raw.Has("include"));
            props["exclude"] = BoolTelemetry(raw.Has("exclude"));
        }

        return new TelemetryEvent
        {
            ProjectInfoTelemetryEvent = new LsProto.ProjectInfoTelemetryEvent
            {
                Properties = props,
                Measurements = CountFileStats(project.Program!.GetSourceFiles()),
            },
        };
    }

    private static void SetTristate(Dictionary<string, object?> m, string key, Tristate v)
    {
        if (v == Tristate.TSTrue)
        {
            m[key] = true;
        }
        else if (v == Tristate.TSFalse)
        {
            m[key] = false;
        }
    }

    private static string BoolTelemetry(bool v)
    {
        if (v)
        {
            return "true";
        }

        return "false";
    }

    private static LsProto.ProjectInfoTelemetryMeasurements CountFileStats(Ast.SourceFile[] sourceFiles)
    {
        var stats = new LsProto.ProjectInfoTelemetryMeasurements();
        foreach (var sf in sourceFiles)
        {
            double size = sf.End();
            switch (sf.ScriptKind)
            {
                case ScriptKind.JS:
                    stats.JsFileCount++;
                    stats.JsFileSize += size;
                    break;
                case ScriptKind.JSX:
                    stats.JsxFileCount++;
                    stats.JsxFileSize += size;
                    break;
                case ScriptKind.TS:
                    if (IsDeclarationFileName(sf.FileName()))
                    {
                        stats.DtsFileCount++;
                        stats.DtsFileSize += size;
                    }
                    else
                    {
                        stats.TsFileCount++;
                        stats.TsFileSize += size;
                    }

                    break;
                case ScriptKind.TSX:
                    stats.TsxFileCount++;
                    stats.TsxFileSize += size;
                    break;
            }
        }

        return stats;
    }

    public Snapshot Snapshot()
    {
        lock (_snapshotMu)
        {
            return _snapshot;
        }
    }

    /// <summary>getSnapshot flushes pending changes and updates the session's snapshot
    /// if needed for the given request. When callerRef is true, the returned
    /// snapshot has an extra reference for the caller (taken atomically under
    /// snapshotMu), guaranteeing it stays alive until the caller calls Deref（session.go:978-1054）。</summary>
    internal Snapshot GetSnapshot(Context ctx, ResourceRequest request, bool callerRef)
    {
        lock (_snapshotUpdateMu)
        {
            CancelScheduledSnapshotUpdate();

            var (fileChanges, overlays, ataChanges, newConfig) = FlushChanges(ctx);
            var updateSnapshotNeeded = !fileChanges.IsEmpty() || ataChanges.Count > 0 || newConfig is not null;
            if (updateSnapshotNeeded)
            {
                // If there are pending file changes, we need to update the snapshot.
                // Sending the requested URI ensures that the project for this URI is loaded.
                var change = new SnapshotChange
                {
                    _reason = UpdateReasonRequestedLanguageServicePendingChanges,
                    _fileChanges = fileChanges,
                    _ataChanges = ataChanges,
                    _newConfig = newConfig,
                };
                change.CopyFrom(request);
                return UpdateSnapshot(ctx, overlays, change, callerRef)!;
            }

            // If there are no pending file changes, we can try to use the current snapshot.
            Snapshot snapshot;
            var updateReason = UpdateReasonUnknown;
            lock (_snapshotMu)
            {
                snapshot = _snapshot;

                if (request.Projects is { Length: > 0 })
                {
                    updateReason = UpdateReasonRequestedLanguageServiceProjectDirty;
                }
                else if (request.ProjectTree is not null)
                {
                    updateReason = UpdateReasonRequestedLoadProjectTree;
                }
                else if (request.AutoImports != default)
                {
                    updateReason = UpdateReasonRequestedLanguageServiceWithAutoImports;
                }
                else
                {
                    foreach (var document in request.Documents ?? [])
                    {
                        var project = snapshot.GetDefaultProject(document);
                        if (project is null)
                        {
                            updateReason = UpdateReasonRequestedLanguageServiceProjectNotLoaded;
                        }
                        else if (project._dirty)
                        {
                            updateReason = UpdateReasonRequestedLanguageServiceProjectDirty;
                        }
                    }

                    if (updateReason == UpdateReasonUnknown)
                    {
                        foreach (var document in request.ConfiguredProjectDocuments ?? [])
                        {
                            if (snapshot.IsOpenFile(document.FileName()))
                            {
                                var project = snapshot.GetDefaultProject(document);
                                if (project is null)
                                {
                                    updateReason = UpdateReasonRequestedLanguageServiceProjectNotLoaded;
                                }
                                else if (project._dirty)
                                {
                                    updateReason = UpdateReasonRequestedLanguageServiceProjectDirty;
                                }
                            }
                            else
                            {
                                updateReason = UpdateReasonRequestedLanguageServiceForFileNotOpen;
                            }
                        }
                    }
                }

                if (updateReason == UpdateReasonUnknown)
                {
                    if (callerRef)
                    {
                        snapshot.Ref();
                    }

                    return snapshot;
                }
            }

            var updateChange = new SnapshotChange { _reason = updateReason };
            updateChange.CopyFrom(request);
            return UpdateSnapshot(ctx, overlays, updateChange, callerRef)!;
        }
    }

    private (Snapshot? Snapshot, Project? Project, Ls.LanguageService? LanguageService, Exception? Error) GetSnapshotAndDefaultProject(
        Context ctx,
        LsProto.DocumentUri uri,
        bool callerRef)
    {
        var snapshot = GetSnapshot(
            ctx,
            new ResourceRequest { Documents = [uri] },
            callerRef);
        var project = snapshot.GetDefaultProject(uri);
        if (project is null)
        {
            if (callerRef)
            {
                snapshot.Deref();
            }

            var file = snapshot.GetFile(uri.FileName());
            if (file is not null && file.Kind() == ScriptKind.Unknown)
            {
                return (null, null, null, new Exception($"{ErrNoProjectForUnknownScriptKind.Message}: no project found for URI {uri}"));
            }

            return (null, null, null, new Exception($"no project found for URI {uri}"));
        }

        return (snapshot, project, Ls.LanguageService.NewLanguageService(project.ID(), project.GetProgram(), snapshot, uri.FileName()), null);
    }

    public (Ls.LanguageService? LanguageService, Exception? Error) GetLanguageService(Context ctx, LsProto.DocumentUri uri)
    {
        var (_, _, languageService, err) = GetSnapshotAndDefaultProject(ctx, uri, false /*callerRef*/);
        if (err is not null)
        {
            return (null, err);
        }

        return (languageService, null);
    }

    public (Project? Project, Ls.LanguageService? LanguageService, Ls.Project[]? Projects, Exception? Error) GetLanguageServiceAndProjectsForFile(Context ctx, LsProto.DocumentUri uri)
    {
        var (snapshot, project, defaultLs, err) = GetSnapshotAndDefaultProject(ctx, uri, false /*callerRef*/);
        if (err is not null)
        {
            return (null, null, null, err);
        }

        // TODO: sheetal:  Get other projects that contain the file with symlink
        var allProjects = snapshot!.GetLanguageServiceProjectsContainingFile(uri);
        return (project, defaultLs, allProjects, null);
    }

    public (Ls.Project[]? Projects, Exception? Error) GetProjectsForFile(Context ctx, LsProto.DocumentUri uri)
    {
        var snapshot = GetSnapshot(
            ctx,
            new ResourceRequest { ConfiguredProjectDocuments = [uri] },
            false /*callerRef*/);

        // TODO: sheetal:  Get other projects that contain the file with symlink
        var allProjects = snapshot.GetLanguageServiceProjectsContainingFile(uri);
        return (allProjects, null);
    }

    /// <summary>GetLanguageServicesForDocumentsLoadingProjectTree returns language services for
    /// every project in the snapshot, loading all project trees first so that projects
    /// that were never opened but reference the given documents are included. Loading the
    /// trees is expensive, so this should only be used by operations that need to touch
    /// every project in a solution, like file rename（session.go:1156-1186）。</summary>
    public Ls.LanguageService[] GetLanguageServicesForDocumentsLoadingProjectTree(Context ctx, LsProto.DocumentUri[] uris)
    {
        var snapshot = GetSnapshot(
            ctx,
            new ResourceRequest
            {
                Documents = uris,
                ProjectTree = new ProjectTreeRequest(),
            },
            false /*callerRef*/);

        var activeFile = "";
        if (uris.Length > 0)
        {
            activeFile = uris[0].FileName();
        }

        var projects = snapshot.ProjectCollection.LanguageServiceProjects();
        var services = new List<Ls.LanguageService>(projects.Length);
        foreach (var project in projects)
        {
            var program = project.GetProgram();
            if (program is null)
            {
                continue;
            }

            services.Add(Ls.LanguageService.NewLanguageService(project.ID(), program, snapshot, activeFile));
        }

        return [.. services];
    }

    public Ls.LanguageService? GetLanguageServiceForProjectWithFile(Context ctx, Project project, LsProto.DocumentUri uri)
    {
        var snapshot = GetSnapshot(
            ctx,
            new ResourceRequest { Projects = [project.ID()] },
            false /*callerRef*/);
        // Ensure we have updated project
        project = snapshot.ProjectCollection.GetProject(project.ID())!;
        if (project is null)
        {
            return null;
        }

        // if program doesnt contain this file any more ignore it
        if (!project.HasFile(uri.FileName()))
        {
            return null;
        }

        return Ls.LanguageService.NewLanguageService(project.ID(), project.GetProgram(), snapshot, uri.FileName());
    }

    /// <summary>WithSnapshotLoadingProjectTree acquires a ref'd snapshot with the
    /// requested project trees loaded, then calls fn. The snapshot stays alive
    /// for the duration of fn（session.go:1194-1205）。</summary>
    public void WithSnapshotLoadingProjectTree(Context ctx, Set<Path>? requestedProjectTrees, Action<Snapshot> fn)
    {
        var snapshot = GetSnapshot(
            ctx,
            new ResourceRequest { ProjectTree = new ProjectTreeRequest(requestedProjectTrees) },
            true /*callerRef*/);
        try
        {
            fn(snapshot);
        }
        finally
        {
            snapshot.Deref();
        }
    }

    public void WithSnapshotForDocument(Context ctx, LsProto.DocumentUri uri, Action<Snapshot> fn)
    {
        var snapshot = GetSnapshot(
            ctx,
            new ResourceRequest { Documents = [uri] },
            true /*callerRef*/);
        try
        {
            fn(snapshot);
        }
        finally
        {
            snapshot.Deref();
        }
    }

    /// <summary>GetCurrentLanguageServiceWithAutoImports flushes pending file changes, clones the
    /// current snapshot with auto-import preparation for the given URI, then returns a
    /// LanguageService for the default project. Use this only outside of request handling
    /// (e.g. cache warming). For request handlers, use GetLanguageServiceWithAutoImports
    /// with the request-level snapshot instead（session.go:1207-1220）。</summary>
    public (Ls.LanguageService? LanguageService, Exception? Error) GetCurrentLanguageServiceWithAutoImports(Context ctx, LsProto.DocumentUri uri)
    {
        var snapshot = GetSnapshot(
            ctx,
            new ResourceRequest
            {
                Documents = [uri],
                AutoImports = uri,
            },
            false /*callerRef*/);
        var project = snapshot.GetDefaultProject(uri);
        if (project is null)
        {
            return (null, new Exception($"no project found for URI {uri}"));
        }

        return (Ls.LanguageService.NewLanguageService(project.ID(), project.GetProgram(), snapshot, uri.FileName()), null);
    }

    /// <summary>WithLanguageServiceAndSnapshot synchronously acquires a ref'd snapshot and
    /// creates a language service for the given URI. fn receives both the language
    /// service and the backing snapshot so it can clone the snapshot (e.g. to
    /// enable auto-imports). The snapshot is kept alive until the async work
    /// completes（session.go:1228-1249）。
    /// Go 的 <c>fn func(*ls.LanguageService, *Snapshot) (func() error, error)</c>：
    /// 异步工作的 `func() error` → `Func<Exception?>`（返回 null 表示成功，非 null 即失败的 error）。</summary>
    public (Func<Exception?>? AsyncWork, Exception? Error) WithLanguageServiceAndSnapshot(
        Context ctx,
        LsProto.DocumentUri uri,
        Func<Ls.LanguageService, Snapshot, (Func<Exception?> Work, Exception? Error)> fn)
    {
        var (snapshot, _, languageService, err) = GetSnapshotAndDefaultProject(ctx, uri, true /*callerRef*/);
        if (err is not null)
        {
            return (null, err);
        }

        var (asyncWork, workError) = fn(languageService!, snapshot!);
        if (workError is not null || asyncWork is null)
        {
            snapshot!.Deref();
            return (null, workError);
        }

        return (() =>
        {
            try
            {
                return asyncWork();
            }
            finally
            {
                snapshot!.Deref();
            }
        }, null);
    }

    /// <summary>GetLanguageServiceWithAutoImports clones the given snapshot with auto-import
    /// preparation for the given URI, without flushing pending file changes.
    /// The cloned snapshot will be adopted as the session's current snapshot in the background
    /// if other changes haven't been adopted in the meantime（session.go:1251-1268）。</summary>
    public (Ls.LanguageService? LanguageService, Exception? Error) GetLanguageServiceWithAutoImports(Context ctx, Snapshot baseSnapshot, LsProto.DocumentUri uri)
    {
        var newSnapshot = CloneSnapshotWithAutoImports(ctx, baseSnapshot, uri, _logger);
        var project = newSnapshot.GetDefaultProject(uri);
        if (project is null)
        {
            // Clone's initial ref (1) is released since we won't use this snapshot.
            newSnapshot.Deref();
            return (null, new Exception($"no project found for URI {uri}"));
        }

        TryAdoptSnapshotChangeInBackground(baseSnapshot, newSnapshot);

        return (Ls.LanguageService.NewLanguageService(project.ID(), project.GetProgram(), newSnapshot, uri.FileName()), null);
    }

    internal void TryAdoptSnapshotChangeInBackground(Snapshot baseSnapshot, Snapshot newSnapshot)
    {
        // The clone's initial ref (1) is transferred to adoptSnapshotChange,
        // which will either promote it as the session's current snapshot or
        // release it if the session has moved on.
        _backgroundQueue.Enqueue(BackgroundContext(), _ => AdoptSnapshotChange(baseSnapshot, newSnapshot));
    }

    /// <summary>adoptSnapshotChange promotes a cloned snapshot as the session's current
    /// snapshot so future requests benefit from the work already done. If the
    /// session has moved on, the snapshot is discarded; the next request needing
    /// auto-imports will redo the work on the latest snapshot（session.go:1279-1315）。</summary>
    internal void AdoptSnapshotChange(Snapshot baseSnapshot, Snapshot newSnapshot)
    {
        lock (_snapshotMu)
        {
            var oldSnapshot = _snapshot;
            if (ReferenceEquals(oldSnapshot, baseSnapshot))
            {
                // Session hasn't moved on; adopt the new snapshot. The clone's initial
                // ref is transferred to become the session's ref for its current snapshot.
                _snapshot = newSnapshot;
                oldSnapshot.Deref();
                var contentMapperTimings = TakeContentMapperTimingDelta();
                if (_options.LoggingEnabled)
                {
                    _logger.Logf("Adopted snapshot {0} (parent {1}) as current session snapshot (replacing {2})", newSnapshot._id, newSnapshot._parentId, oldSnapshot._id);
                    if (newSnapshot._builderLogs is not null)
                    {
                        _logger.Log(newSnapshot._builderLogs.String());
                    }

                    LogContentMapperTimings(contentMapperTimings);
                }
            }
            else
            {
                // Session has moved on to a newer snapshot; discard this one.
                // Release the clone's initial ref. If a handler is still using
                // the snapshot, its own ref keeps it alive.
                if (_options.LoggingEnabled)
                {
                    _logger.Logf("Discarded snapshot {0} (parent {1}); session has moved on to snapshot {2}", newSnapshot._id, newSnapshot._parentId, oldSnapshot._id);
                    if (newSnapshot._builderLogs is not null)
                    {
                        var discardedLogs = newSnapshot._builderLogs.String();
                        if (discardedLogs != "")
                        {
                            _logger.Logf("--- Discarded snapshot {0} builder logs (NOT adopted) ---", newSnapshot._id);
                            _logger.Log(discardedLogs);
                            _logger.Logf("--- End discarded snapshot {0} builder logs ---", newSnapshot._id);
                        }
                    }
                }

                newSnapshot.Deref();
            }
        }
    }

    public void UpdateSnapshot(Context ctx, Dictionary<Path, Overlay> overlays, SnapshotChange change)
    {
        UpdateSnapshot(ctx, overlays, change, false);
    }

    /// <summary>updateSnapshotRef is like UpdateSnapshot but returns the created snapshot
    /// with an extra reference for the caller. The ref is taken atomically with
    /// the snapshot assignment under snapshotMu, so the snapshot is guaranteed
    /// to be alive when returned. The caller must call snapshot.Deref() when done（session.go:1325-1330）。</summary>
    internal Snapshot UpdateSnapshotRef(Context ctx, Dictionary<Path, Overlay> overlays, SnapshotChange change)
    {
        return UpdateSnapshot(ctx, overlays, change, true)!;
    }

    internal Snapshot? UpdateSnapshot(Context ctx, Dictionary<Path, Overlay> overlays, SnapshotChange change, bool callerRef)
    {
        Snapshot oldSnapshot;
        Snapshot newSnapshot;
        ContentMapper.Timings contentMapperTimings;
        lock (_snapshotMu)
        {
            oldSnapshot = _snapshot;
            if (!Loc.HasLocale(ctx))
            {
                ctx = WithCurrentLocale(ctx);
            }

            newSnapshot = oldSnapshot.Clone(ctx, change, overlays, _logger, _client);

            // A failed API request may have mutated only a prefix of its clone. Such a
            // snapshot is returned to the caller for inspection and cleanup, but must
            // never become canonical session state or trigger adoption side effects.
            if (newSnapshot._apiError is not null)
            {
                if (callerRef)
                {
                    return newSnapshot;
                }

                newSnapshot.Deref();
                return null;
            }

            _snapshot = newSnapshot;
            if (callerRef)
            {
                newSnapshot.Ref();
            }

            contentMapperTimings = new ContentMapper.Timings();
            if (!ReferenceEquals(newSnapshot, oldSnapshot))
            {
                // Release the session's reference to the old snapshot. The new snapshot's
                // clone ref (1) is transferred to become the session's ref for its current
                // snapshot. Other holders (e.g. active handlers) keep the old snapshot alive
                // via their own refs until they complete.
                oldSnapshot.Deref();
                contentMapperTimings = TakeContentMapperTimingDelta();
            }
        }

        // Enqueue ATA updates if needed
        if (_typingsInstaller is not null && !Config().IsATADisabled())
        {
            TriggerATAForUpdatedProjects(newSnapshot);
        }

        // Enqueue logging, watch updates, and diagnostic refresh tasks
        // TODO: userPreferences/configuration updates
        _backgroundQueue.Enqueue(BackgroundContext(), ctx =>
        {
            if (_options.LoggingEnabled)
            {
                _logger.Logf("Adopted snapshot {0} (parent {1}) as current session snapshot (replacing {2})", newSnapshot._id, newSnapshot._parentId, oldSnapshot._id);
                if (newSnapshot._builderLogs is not null)
                {
                    _logger.Log(newSnapshot._builderLogs.String());
                }

                LogProjectChanges(oldSnapshot, newSnapshot);
                LogContentMapperTimings(contentMapperTimings);
                _logger.Log("");
            }

            if (_options.WatchEnabled)
            {
                var watchError = UpdateWatches(oldSnapshot, newSnapshot);
                if (watchError is not null && _options.LoggingEnabled)
                {
                    _logger.Log(watchError.Message);
                }
            }

            UpdateContentMapperRegistrations(ctx, newSnapshot);
            PublishProgramDiagnostics(oldSnapshot, newSnapshot);
            SendProjectInfoTelemetryForNewProjects(oldSnapshot, newSnapshot);
            WarmAutoImportCache(ctx, change, oldSnapshot, newSnapshot);
        });

        return newSnapshot;
    }

    private ContentMapper.Timings TakeContentMapperTimingDelta()
    {
        if (_contentMapperHost is null)
        {
            return new ContentMapper.Timings();
        }

        var current = _contentMapperHost.Timings();
        lock (_contentMapperTimingsMu)
        {
            var delta = current.Since(_contentMapperTimings);
            _contentMapperTimings = current;
            return delta;
        }
    }

    private void LogContentMapperTimings(ContentMapper.Timings timings)
    {
        if (timings.RequestWait == TimeSpan.Zero && !HasContentMapperOperationTimings(timings.Mappers))
        {
            return;
        }

        _logger.Log("Content mapper timings since previous snapshot adoption:");
        if (timings.RequestWait != TimeSpan.Zero)
        {
            _logger.Logf("  Request wait time: {0}", timings.RequestWait);
        }

        // Go: `for _, identity := range slices.Sorted(maps.Keys(timings.Mappers))`
        var identities = new List<string>(timings.Mappers.Keys);
        identities.Sort(StringComparer.Ordinal);
        foreach (var identity in identities)
        {
            var mapper = timings.Mappers[identity];
            if (!HasContentMapperOperationTiming(mapper))
            {
                continue;
            }

            _logger.Logf("  {0}:", identity);
            if (mapper.Spawn.Count != 0)
            {
                _logger.Logf("    Initializations: {0} ({1})", mapper.Spawn.Count, mapper.Spawn.Duration + mapper.Initialize.Duration);
            }

            if (mapper.OpenProject.Count != 0)
            {
                _logger.Logf("    openProject requests: {0} ({1})", mapper.OpenProject.Count, mapper.OpenProject.Duration);
            }

            if (mapper.CloseProject.Count != 0)
            {
                _logger.Logf("    closeProject requests: {0} ({1})", mapper.CloseProject.Count, mapper.CloseProject.Duration);
            }

            if (mapper.Transform.Count != 0)
            {
                _logger.Logf("    Transforms: {0} ({1})", mapper.Transform.Count, mapper.Transform.Duration);
            }
        }
    }

    private static bool HasContentMapperOperationTimings(Dictionary<string, ContentMapper.MapperTimings> timings)
    {
        foreach (var timing in timings.Values)
        {
            if (HasContentMapperOperationTiming(timing))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasContentMapperOperationTiming(ContentMapper.MapperTimings timing)
    {
        return timing.Spawn.Count != 0 || timing.OpenProject.Count != 0 || timing.CloseProject.Count != 0 || timing.Transform.Count != 0;
    }

    /// <summary>WaitForBackgroundTasks waits for all background tasks to complete.
    /// This is intended to be used only for testing purposes（session.go:1448-1451）。</summary>
    public void WaitForBackgroundTasks()
    {
        CancelIdleCacheClean();
        _backgroundQueue.Wait();
    }

    /// <summary>Go: <c>func (s *Session) updateWatch[T any](ctx, oldWatcher, newWatcher) []error</c>（session.go:1453-1533）。</summary>
    internal List<Exception> UpdateWatch<T>(Context ctx, WatchedFiles<T>? oldWatcher, WatchedFiles<T>? newWatcher)
    {
        var errors = new List<Exception>();
        if (newWatcher is not null)
        {
            var w = newWatcher.Watchers();
            var watchers = new List<LsProto.FileSystemWatcher>(w.WorkspaceWatchers.Length + w.OutsideWorkspaceWatchers.Length);
            watchers.AddRange(w.WorkspaceWatchers);
            watchers.AddRange(w.OutsideWorkspaceWatchers);
            if (watchers.Count > 0)
            {
                var newWatchers = new OrderedMap<WatcherID, LsProto.FileSystemWatcher>();
                for (var i = 0; i < watchers.Count; i++)
                {
                    var watcher = watchers[i];
                    var globId = new WatcherID($"{w.WatcherID.Value}.{i}");
                    if (_watches!.Acquire(watcher, globId))
                    {
                        newWatchers.Set(globId, watcher);
                    }
                }

                var watchErrors = new List<Exception>();
                foreach (var (id, watcher) in newWatchers.Entries())
                {
                    // Create a fresh timeout per client call so earlier calls
                    // don't consume the deadline for later ones.
                    var callCts = new CancellationTokenSource(_watchRequestTimeout);
                    var callCtx = WithCancellation(ctx, callCts.Token);
                    Exception? watchError = null;
                    try
                    {
                        _client!.WatchFiles(callCtx, id, [watcher!]);
                    }
                    catch (Exception e)
                    {
                        watchError = e;
                    }

                    callCts.Cancel();
                    if (watchError is not null)
                    {
                        watchErrors.Add(watchError);
                    }
                    else
                    {
                        _logger.Log(oldWatcher is null ? $"Added new watch: {id}" : $"Updated watch: {id}");
                        _logger.Log("\t" + Watch.FileSystemWatcherGlobString(watcher!));
                        _logger.Log("");
                    }
                }

                if (watchErrors.Count > 0)
                {
                    // Roll back ALL newly-acquired watchers on any failure to keep
                    // refcounts clean. On retry, Acquire will see them as new again.
                    // Re-registering an already-registered watcher with the client
                    // is harmless (registerCapability with the same ID replaces it).
                    foreach (var (_, watcher) in newWatchers.Entries())
                    {
                        _watches!.Release(watcher!);
                    }

                    _watches!.MarkPending(w.WatcherID);
                    errors.AddRange(watchErrors);
                }
                else
                {
                    _watches!.ClearPending(w.WatcherID);
                }

                if (w.IgnoredPaths.Count > 0)
                {
                    _logger.Logf("{0} paths ineligible for watching", w.IgnoredPaths.Count);
                    if (_logger.IsVerbose())
                    {
                        foreach (var path in w.IgnoredPaths)
                        {
                            _logger.Log("\t" + path);
                        }
                    }
                }
            }
        }

        if (oldWatcher is not null)
        {
            var w = oldWatcher.Watchers();
            var watchers = new List<LsProto.FileSystemWatcher>(w.WorkspaceWatchers.Length + w.OutsideWorkspaceWatchers.Length);
            watchers.AddRange(w.WorkspaceWatchers);
            watchers.AddRange(w.OutsideWorkspaceWatchers);
            if (watchers.Count > 0)
            {
                var removedIDs = new List<WatcherID>();
                foreach (var watcher in watchers)
                {
                    var (id, removed) = _watches!.Release(watcher);
                    if (removed)
                    {
                        removedIDs.Add(id);
                    }
                }

                foreach (var id in removedIDs)
                {
                    var callCts = new CancellationTokenSource(_watchRequestTimeout);
                    var callCtx = WithCancellation(ctx, callCts.Token);
                    Exception? unwatchError = null;
                    try
                    {
                        _client!.UnwatchFiles(callCtx, id);
                    }
                    catch (Exception e)
                    {
                        unwatchError = e;
                    }

                    callCts.Cancel();
                    if (unwatchError is not null)
                    {
                        errors.Add(unwatchError);
                    }
                    else if (newWatcher is null)
                    {
                        _logger.Log($"Removed watch: {id}");
                    }
                }
            }
        }

        return errors;
    }

    /// <summary>updateContentMapperRegistrations computes the union of content mapper extensions across all loaded
    /// configs in the new snapshot and, when the set changes, asks the client to synchronize text documents
    /// with those extensions. This is how an otherwise unsupported file (e.g. a `.vue`) begins flowing to the server once a
    /// config that maps it is discovered（session.go:1535-1577）。</summary>
    internal Exception? UpdateContentMapperRegistrations(Context ctx, Snapshot snapshot)
    {
        if (_client is null)
        {
            return null;
        }

        var contentMappers = snapshot.ConfigFileRegistry.ContentMappers();
        var extensions = new List<string>(contentMappers._extensions);
        extensions.AddRange(snapshot._inferredProjectContentMapperExtensions);
        extensions.Sort(StringComparer.Ordinal);
        var sortedExtensions = DeduplicateSorted([.. extensions], static (a, b) => string.Equals(a, b, StringComparison.Ordinal));

        lock (_contentMapperRegistrationMu)
        {
            // Background tasks may finish out of order; never let an older snapshot's task overwrite the
            // registration derived from a newer one.
            if (snapshot.ID() <= _registeredContentMapperSnapshotID)
            {
                return null;
            }

            if (Same(sortedExtensions, _registeredContentMapperExtensions))
            {
                _registeredContentMapperSnapshotID = snapshot.ID();
                return null;
            }

            // RegisterContentMapperExtensions replaces the prior registration wholesale (unregistering extensions
            // that are no longer mapped and registering the current set), so an empty set removes the registration
            // once the last mapping config unloads. On failure we leave the state unadvanced so the next snapshot
            // update retries.
            try
            {
                _client.RegisterContentMapperExtensions(ctx, sortedExtensions);
            }
            catch (Exception e)
            {
                if (_options.LoggingEnabled)
                {
                    _logger.Log(e.Message);
                }

                return e;
            }

            _registeredContentMapperExtensions = sortedExtensions;
            _registeredContentMapperSnapshotID = snapshot.ID();
            return null;
        }
    }

    internal Exception? UpdateWatches(Snapshot oldSnapshot, Snapshot newSnapshot)
    {
        var errors = new List<Exception>();
        var start = Stopwatch.GetTimestamp();
        var ctx = BackgroundContext();
        DiffMapsFunc(
            oldSnapshot.ConfigFileRegistry._configs,
            newSnapshot.ConfigFileRegistry._configs,
            static (a, b) => a._rootFilesWatch!.ID() == b._rootFilesWatch!.ID(),
            (_, addedEntry) => errors.AddRange(UpdateWatch(ctx, null, addedEntry._rootFilesWatch)),
            (_, removedEntry) => errors.AddRange(UpdateWatch(ctx, removedEntry._rootFilesWatch, null)),
            (_, oldEntry, newEntry) => errors.AddRange(UpdateWatch(ctx, oldEntry._rootFilesWatch, newEntry._rootFilesWatch)));

        // Retry config watchers whose IDs didn't change but whose previous registration failed.
        foreach (var (path, newEntry) in newSnapshot.ConfigFileRegistry._configs)
        {
            if (oldSnapshot.ConfigFileRegistry._configs.TryGetValue(path, out var oldEntry))
            {
                if (oldEntry._rootFilesWatch!.ID() == newEntry._rootFilesWatch!.ID() && _watches!.IsPending(newEntry._rootFilesWatch.ID()))
                {
                    errors.AddRange(UpdateWatch(ctx, null, newEntry._rootFilesWatch));
                }
            }
        }

        DiffOrderedMaps(
            oldSnapshot.ProjectCollection.ProjectsByID(),
            newSnapshot.ProjectCollection.ProjectsByID(),
            (_, addedProject) =>
            {
                errors.AddRange(UpdateWatch(ctx, null, addedProject!._programFilesWatch));
                errors.AddRange(UpdateWatch(ctx, null, addedProject._typingsWatch));
                errors.AddRange(UpdateWatch(ctx, null, addedProject._contentMapperWatch));
            },
            (_, removedProject) =>
            {
                errors.AddRange(UpdateWatch(ctx, removedProject!._programFilesWatch, null));
                errors.AddRange(UpdateWatch(ctx, removedProject._typingsWatch, null));
                errors.AddRange(UpdateWatch(ctx, removedProject._contentMapperWatch, null));
            },
            (_, oldProject, newProject) =>
            {
                if (oldProject!._programFilesWatch?.ID() != newProject!._programFilesWatch?.ID())
                {
                    errors.AddRange(UpdateWatch(ctx, oldProject._programFilesWatch, newProject._programFilesWatch));
                }
                else if (_watches!.IsPending(newProject._programFilesWatch!.ID()))
                {
                    errors.AddRange(UpdateWatch(ctx, null, newProject._programFilesWatch));
                }

                if (oldProject._typingsWatch?.ID() != newProject._typingsWatch?.ID())
                {
                    errors.AddRange(UpdateWatch(ctx, oldProject._typingsWatch, newProject._typingsWatch));
                }
                else if (_watches!.IsPending(newProject._typingsWatch!.ID()))
                {
                    errors.AddRange(UpdateWatch(ctx, null, newProject._typingsWatch));
                }

                if (oldProject._contentMapperWatch?.ID() != newProject._contentMapperWatch?.ID())
                {
                    errors.AddRange(UpdateWatch(ctx, oldProject._contentMapperWatch, newProject._contentMapperWatch));
                }
                else if (_watches!.IsPending(newProject._contentMapperWatch!.ID()))
                {
                    errors.AddRange(UpdateWatch(ctx, null, newProject._contentMapperWatch));
                }
            });

        if (oldSnapshot._autoImportsWatch!.ID() != newSnapshot._autoImportsWatch!.ID())
        {
            errors.AddRange(UpdateWatch(ctx, oldSnapshot._autoImportsWatch, newSnapshot._autoImportsWatch));
        }
        else if (_watches!.IsPending(newSnapshot._autoImportsWatch.ID()))
        {
            errors.AddRange(UpdateWatch(ctx, null, newSnapshot._autoImportsWatch));
        }

        if (errors.Count > 0)
        {
            return new Exception($"errors updating watches: {FormatGoSlice(errors.Select(static e => e.Message))}");
        }

        if (_options.LoggingEnabled)
        {
            _logger.Log($"Updated watches in {Stopwatch.GetElapsedTime(start)}");
        }

        return null;
    }

    public new void Close()
    {
        // Cancel any pending scheduled snapshot update
        CancelScheduledSnapshotUpdate();
        // Cancel any pending diagnostics refresh
        CancelDiagnosticsRefresh();
        // Cancel any pending auto-import cache warming
        CancelWarmAutoImportCache();
        // Cancel any pending idle cache clean
        CancelIdleCacheClean();
        // Cancel periodic performance telemetry
        StopPerformanceTelemetry();
        _backgroundQueue.Close();
        base.Close();
    }

    internal (FileChangeSummary FileChanges, Dictionary<Path, Overlay> Overlays, Dictionary<ID, ATAStateChange> AtaChanges, UserPreferences? NewConfig) FlushChanges(Context ctx)
    {
        lock (_pendingFileChangesMu)
        {
            lock (_pendingATAChangesMu)
            {
                var pendingATAChanges = _pendingATAChanges;
                _pendingATAChanges = [];
                var (fileChanges, overlays) = FlushChangesLocked(ctx);
                lock (_userConfigRWMu)
                {
                    UserPreferences? newPrefs = null;
                    if (_pendingUserConfigChanges)
                    {
                        newPrefs = _workspaceUserPreferences;
                    }

                    _pendingUserConfigChanges = false;
                    return (fileChanges, overlays, pendingATAChanges, newPrefs);
                }
            }
        }
    }

    /// <summary>flushChangesLocked should only be called with s.pendingFileChangesMu held（session.go:1691-1704）。</summary>
    private (FileChangeSummary FileChanges, Dictionary<Path, Overlay> Overlays) FlushChangesLocked(Context ctx)
    {
        if (_pendingFileChanges.Count == 0)
        {
            return (new FileChangeSummary(), _fs.Overlays());
        }

        var start = Stopwatch.GetTimestamp();
        var (changes, overlays) = _fs.ProcessChanges([.. _pendingFileChanges]);
        if (_options.LoggingEnabled)
        {
            _logger.Log($"Processed {_pendingFileChanges.Count} file changes in {Stopwatch.GetElapsedTime(start)}");
        }

        _pendingFileChanges = [];
        return (changes, overlays);
    }

    /// <summary>logProjectChanges logs information about projects that have changed between snapshots（session.go:1706-1735）。</summary>
    private void LogProjectChanges(Snapshot oldSnapshot, Snapshot newSnapshot)
    {
        var loggedProjectChanges = false;
        void LogProject(Project project)
        {
            var builder = new StringBuilder();
            project.Print(_logger.IsVerbose() /*writeFileNames*/, _logger.IsVerbose() /*writeFileExplanation*/, builder);
            _logger.Log(builder.ToString());
            loggedProjectChanges = true;
        }

        DiffOrderedMaps(
            oldSnapshot.ProjectCollection.ProjectsByID(),
            newSnapshot.ProjectCollection.ProjectsByID(),
            (_, addedProject) => LogProject(addedProject!),
            (_, removedProject) => _logger.Logf("\nProject '{0}' removed\n{1}", removedProject!.ID().Value, ProjectConstants.Hr),
            (_, _, newProject) =>
            {
                // Project updated
                if (newProject!.ProgramUpdateKind == ProgramUpdateKindNewFiles)
                {
                    LogProject(newProject);
                }
            });

        if (loggedProjectChanges || _logger.IsVerbose())
        {
            LogCacheStats(newSnapshot);
        }
    }

    public byte[] NpmInstall(string cwd, string[] npmInstallArgs)
    {
        return _npmExecutor!.NpmInstall(cwd, npmInstallArgs);
    }

    private void RefreshInlayHintsIfNeeded(UserPreferences oldPrefs, UserPreferences newPrefs)
    {
        if (oldPrefs.InlayHints != newPrefs.InlayHints)
        {
            try
            {
                _client!.RefreshInlayHints(BackgroundContext());
            }
            catch (Exception e)
            {
                if (_options.LoggingEnabled)
                {
                    _logger.Logf("Error refreshing inlay hints: {0}", e.Message);
                }
            }
        }
    }

    private void RefreshCodeLensIfNeeded(UserPreferences oldPrefs, UserPreferences newPrefs)
    {
        if (oldPrefs.CodeLens != newPrefs.CodeLens)
        {
            try
            {
                _client!.RefreshCodeLens(BackgroundContext());
            }
            catch (Exception e)
            {
                if (_options.LoggingEnabled)
                {
                    _logger.Logf("Error refreshing code lens: {0}", e.Message);
                }
            }
        }
    }

    private void RefreshDiagnosticsIfNeeded(UserPreferences oldPrefs, UserPreferences newPrefs)
    {
        if (oldPrefs.CustomConfigFileName != newPrefs.CustomConfigFileName ||
            oldPrefs.ReportStyleChecksAsWarnings != newPrefs.ReportStyleChecksAsWarnings ||
            oldPrefs.EnableValidation != newPrefs.EnableValidation)
        {
            ScheduleDiagnosticsRefresh();
        }
    }

    private void RefreshATAIfNeeded(UserPreferences oldPrefs, UserPreferences newPrefs)
    {
        if (oldPrefs.IsATADisabled() && !newPrefs.IsATADisabled())
        {
            // ATA was re-enabled; schedule a diagnostics refresh so the next snapshot update
            // re-triggers ATA for existing projects with the new setting.
            ScheduleDiagnosticsRefresh();
        }
    }

    private void LogCacheStats(Snapshot snapshot)
    {
        var parseCacheSize = 0;
        var extendedConfigCount = 0;
        if (_logger.IsVerbose())
        {
            _parseCache._entries.Range((_, _) =>
            {
                parseCacheSize++;
                return true;
            });
            _extendedConfigCache._entries.Range((_, _) =>
            {
                extendedConfigCount++;
                return true;
            });
        }

        _logger.Log("\n======== Cache Statistics ========");
        _logger.Logf("Open file count:   {0,6}", snapshot.Overlays().Count);
        _logger.Logf("Cached disk files: {0,6}", snapshot._fs._cacheFiles?.Count ?? 0);
        _logger.Logf("Realpath aliases:  {0,6}", snapshot._fs._nodeModulesRealpathAliases?.Count ?? 0);
        _logger.Logf("Project count:     {0,6}", snapshot.ProjectCollection.Projects().Length);
        _logger.Logf("Config count:      {0,6}", snapshot.ConfigFileRegistry._configs.Count);
        if (_logger.IsVerbose())
        {
            _logger.Logf("Parse cache size:           {0,6}", parseCacheSize);
            _logger.Logf("Program count:              {0,6}", _programCounter.Len());
            _logger.Logf("Extended config cache size: {0,6}", extendedConfigCount);

            _logger.Log("Auto Imports:");
            var autoImportStats = snapshot.AutoImportRegistry().GetCacheStats();
            _logger.Logf("\tUnique packages (by realpath): {0}", autoImportStats.UniquePackageCount);
            if (autoImportStats.ProjectBuckets.Count > 0)
            {
                _logger.Log("\tProject buckets:");
                foreach (var bucket in autoImportStats.ProjectBuckets)
                {
                    _logger.Logf("\t\t{0}{1}:", bucket.Name, IfElse(bucket.State.Dirty(), " (dirty)", ""));
                    _logger.Logf("\t\t\tFiles: {0}", bucket.FileCount);
                    _logger.Logf("\t\t\tExports: {0}", bucket.ExportCount);
                }
            }

            if (autoImportStats.NodeModulesBuckets.Count > 0)
            {
                _logger.Log("\tnode_modules buckets:");
                foreach (var bucket in autoImportStats.NodeModulesBuckets)
                {
                    _logger.Logf("\t\t{0}{1}:", bucket.Name, IfElse(bucket.State.Dirty(), " (dirty)", ""));
                    if (bucket.State.DirtyPackages() is { } dirtyPackages)
                    {
                        foreach (var packageName in dirtyPackages.Keys())
                        {
                            _logger.Logf("\t\t\tNeeds granular update: {0}", packageName);
                        }
                    }

                    if (bucket.DependencyNames is not null)
                    {
                        _logger.Logf("\t\t\tCollected packages: {0}", bucket.DependencyNames.Len());
                    }
                    else
                    {
                        _logger.Log("\t\t\tCollected packages: all, due to no package.json!");
                    }

                    _logger.Logf("\t\t\tTotal packages: {0}", bucket.PackageNames!.Len());
                    _logger.Logf("\t\t\tFiles: {0}", bucket.FileCount);
                    _logger.Logf("\t\t\tExports: {0}", bucket.ExportCount);
                    if (bucket.State.RecursiveSearchPackages() is null)
                    {
                        _logger.Log("\t\t\tRecursive search: all");
                    }
                    else if (bucket.State.RecursiveSearchPackages()!.Len() > 0)
                    {
                        _logger.Logf("\t\t\tRecursive search: {0} packages", bucket.State.RecursiveSearchPackages()!.Len());
                    }
                    else
                    {
                        _logger.Log("\t\t\tRecursive search: none");
                    }
                }
            }
        }
    }

    private void PublishProgramDiagnostics(Snapshot oldSnapshot, Snapshot newSnapshot)
    {
        if (!_options.PushDiagnosticsEnabled)
        {
            return;
        }

        if (newSnapshot.UserPreferences().EnableValidation.IsFalse())
        {
            if (oldSnapshot.UserPreferences().EnableValidation.IsFalse())
            {
                return;
            }

            foreach (var (_, oldProject) in oldSnapshot.ProjectCollection.ProjectsByID().Entries())
            {
                var configured = oldProject!.ID().TryConfigured(out var configuredID);
                if (configured && oldSnapshot.ProjectCollection.GetOpenConfiguredProjects().Has(configuredID))
                {
                    var configFilePath = oldProject.ConfigFilePath();
                    PublishProjectDiagnostics(BackgroundContext(), configFilePath.Value, null, oldSnapshot._converters);
                }
            }

            return;
        }

        var ctx = BackgroundContext();
        var oldProjects = oldSnapshot.ProjectCollection.ProjectsByID();
        var newProjects = newSnapshot.ProjectCollection.ProjectsByID();
        var oldOpenProjects = oldSnapshot.ProjectCollection.GetOpenConfiguredProjects();
        var newOpenProjects = newSnapshot.ProjectCollection.GetOpenConfiguredProjects();
        DiffOrderedMaps(
            oldProjects,
            newProjects,
            (_, addedProject) =>
            {
                var configured = addedProject!.ID().TryConfigured(out var configuredID);
                if (!ShouldPublishProgramDiagnostics(addedProject, newSnapshot.ID()) || !configured || !newOpenProjects.Has(configuredID))
                {
                    return;
                }

                var configFilePath = addedProject.ConfigFilePath();
                PublishProjectDiagnostics(ctx, configFilePath.Value, addedProject.GetProjectDiagnostics(ctx), newSnapshot._converters);
            },
            (_, removedProject) =>
            {
                if (removedProject!.Kind != KindConfigured)
                {
                    return;
                }

                var configFilePath = removedProject.ConfigFilePath();
                PublishProjectDiagnostics(ctx, configFilePath.Value, null, oldSnapshot._converters);
            },
            (_, _, newProject) =>
            {
                var configured = newProject!.ID().TryConfigured(out var configuredID);
                if (!ShouldPublishProgramDiagnostics(newProject, newSnapshot.ID()) || !configured || !newOpenProjects.Has(configuredID))
                {
                    return;
                }

                var configFilePath = newProject.ConfigFilePath();
                PublishProjectDiagnostics(ctx, configFilePath.Value, newProject.GetProjectDiagnostics(ctx), newSnapshot._converters);
            });

        // Sync diagnostics for projects whose open-file state changed without a program update.
        foreach (var (projectID, newProject) in newProjects.Entries())
        {
            if (newProject!.Kind != KindConfigured)
            {
                continue;
            }

            if (!oldProjects.Has(projectID))
            {
                continue; // Handled by added project case above
            }

            newProject.ID().TryConfigured(out var configuredID);
            var configFilePath = newProject.ConfigFilePath();
            var (oldProject, _) = oldProjects.Get(projectID);
            var newHasOpenFiles = newOpenProjects.Has(configuredID);
            var oldHasOpenFiles = oldOpenProjects.Has(configuredID);
            if (newHasOpenFiles && !oldHasOpenFiles &&
                (ReferenceEquals(newProject, oldProject) || !ShouldPublishProgramDiagnostics(newProject, newSnapshot.ID())))
            {
                // Project reopened without a program update
                PublishProjectDiagnostics(ctx, configFilePath.Value, newProject.GetProjectDiagnostics(ctx), newSnapshot._converters);
            }
            else if (!newHasOpenFiles && oldHasOpenFiles)
            {
                // Project closed
                PublishProjectDiagnostics(ctx, configFilePath.Value, null, newSnapshot._converters);
            }
        }
    }

    private static bool ShouldPublishProgramDiagnostics(Project p, ulong snapshotID)
    {
        if (p.Kind != KindConfigured || p.Program is null || p.ProgramLastUpdate != snapshotID)
        {
            return false;
        }

        return p.ProgramUpdateKind > ProgramUpdateKindCloned;
    }

    private void PublishProjectDiagnostics(Context ctx, string configFilePath, Ast.Diagnostic[]? diagnostics, LsConv.Converters converters)
    {
        if (Config().EnableValidation.IsFalse())
        {
            diagnostics = null;
        }

        ctx = WithCurrentLocale(ctx);
        var lspDiagnostics = new List<LsProto.Diagnostic>(diagnostics?.Length ?? 0);
        foreach (var diag in diagnostics ?? [])
        {
            lspDiagnostics.Add(LsConv.LsConv.DiagnosticToLSPPush(ctx, converters, diag));
        }

        try
        {
            _client!.PublishDiagnostics(ctx, new LsProto.PublishDiagnosticsParams
            {
                Uri = LsConv.LsConv.FileNameToDocumentURI(configFilePath),
                Diagnostics = [.. lspDiagnostics],
            });
        }
        catch (Exception e)
        {
            if (_options.LoggingEnabled)
            {
                _logger.Logf("Error publishing diagnostics: {0}", e.Message);
            }
        }
    }

    /// <summary>EnqueuePublishGlobalDiagnostics schedules a background check for new accumulated
    /// global diagnostics from checker pools, re-publishing tsconfig diagnostics if changed.
    /// Multiple calls are coalesced into a single background task（session.go:2035-2042）。</summary>
    public void EnqueuePublishGlobalDiagnostics()
    {
        if (!_options.PushDiagnosticsEnabled || Config().EnableValidation.IsFalse())
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _globalDiagPublishPending, 1, 0) == 0)
        {
            _backgroundQueue.Enqueue(BackgroundContext(), PublishGlobalDiagnostics);
        }
    }

    internal void PublishGlobalDiagnostics(Context ctx)
    {
        try
        {
            Snapshot snapshot;
            lock (_snapshotMu)
            {
                snapshot = _snapshot;
                snapshot.Ref();
            }

            try
            {
                foreach (var project in snapshot.ProjectCollection.Projects())
                {
                    if (project.Kind != KindConfigured || project._checkerPool is null)
                    {
                        continue;
                    }

                    if (project._checkerPool.TakeNewGlobalDiagnostics())
                    {
                        PublishProjectDiagnostics(ctx, project._configFilePath.Value, project.GetProjectDiagnostics(ctx), snapshot._converters);
                    }
                }
            }
            finally
            {
                snapshot.Deref();
            }
        }
        finally
        {
            Interlocked.Exchange(ref _globalDiagPublishPending, 0);
        }
    }

    private void TriggerATAForUpdatedProjects(Snapshot newSnapshot)
    {
        foreach (var project in newSnapshot.ProjectCollection.Projects())
        {
            if (project.ShouldTriggerATA(newSnapshot.ID()))
            {
                var projectToUpdate = project;
                _backgroundQueue.Enqueue(BackgroundContext(), ctx =>
                {
                    LogTree? logTree = null;
                    if (_options.LoggingEnabled)
                    {
                        logTree = NewLogTree("Triggering ATA for project " + projectToUpdate.ID().Value);
                    }

                    var typingsInfo = projectToUpdate.ComputeTypingsInfo();
                    var request = new Ata.TypingsInstallRequest
                    {
                        ProjectID = projectToUpdate.ID(),
                        TypingsInfo = typingsInfo,
                        FileNames = Map(projectToUpdate.Program!.GetSourceFiles(), static file => file.FileName()),
                        ProjectRootPath = projectToUpdate._currentDirectory,
                        CompilerOptions = projectToUpdate.CommandLine!.CompilerOptions()!,
                        CurrentDirectory = _options.CurrentDirectory,
                        GetScriptKind = GetScriptKindFromFileName,
                        FS = _fs,
                        Logger = logTree,
                    };

                    var projectDisplayName = projectToUpdate.DisplayName(_options.CurrentDirectory);
                    if (_client is not null)
                    {
                        _client.ProgressStart(Installing_types_for_0, projectDisplayName);
                    }

                    Ata.TypingsInstallResult? result = null;
                    Exception? installError = null;
                    try
                    {
                        result = _typingsInstaller!.InstallTypings(request);
                    }
                    catch (Exception e)
                    {
                        installError = e;
                    }

                    if (_client is not null)
                    {
                        _client.ProgressFinish(Installing_types_for_0, projectDisplayName);
                    }

                    if (installError is not null)
                    {
                        if (logTree is not null)
                        {
                            _logger.Log($"ATA installation failed for project {projectToUpdate.ID().Value}: {installError.Message}");
                            _logger.Log(logTree.String());
                        }
                    }
                    else if (!Same(result!.TypingsFiles, projectToUpdate._typingsFiles))
                    {
                        lock (_pendingATAChangesMu)
                        {
                            _pendingATAChanges[projectToUpdate.ID()] = new ATAStateChange
                            {
                                TypingsInfo = typingsInfo,
                                TypingsFiles = result.TypingsFiles,
                                TypingsFilesToWatch = result.FilesToWatch,
                                Logs = logTree,
                            };
                        }

                        ScheduleDiagnosticsRefresh();
                    }
                });
            }
        }
    }

    private void WarmAutoImportCache(Context ctx, SnapshotChange change, Snapshot oldSnapshot, Snapshot newSnapshot)
    {
        if (change._fileChanges.Changed.Len() == 1)
        {
            var changedFile = default(LsProto.DocumentUri);
            foreach (var uri in change._fileChanges.Changed.Keys())
            {
                changedFile = uri;
            }

            if (!newSnapshot.IsOpenFile(changedFile.FileName()))
            {
                return;
            }

            var prefs = newSnapshot.UserPreferences();
            if (prefs.IncludeCompletionsForModuleExports.IsFalse())
            {
                return;
            }

            var project = newSnapshot.GetDefaultProject(changedFile);
            if (project is null)
            {
                return;
            }

            if (newSnapshot.AutoImports!.IsPreparedForImportingFile(changedFile.FileName(), project.ID(), prefs))
            {
                return;
            }

            // Cancel any previous auto-import warming and create a new cancellable context.
            // Only publish the new cancel func if the derived context is still active,
            // and make the stored cancel func a no-op once that warming task is done.
            var warmCts = new CancellationTokenSource();
            var warmCtx = WithCancellation(ctx, warmCts.Token);
            lock (_warmAutoImportMu)
            {
                _warmAutoImportCancel?.Invoke();
                if (!warmCtx.CancellationToken.IsCancellationRequested)
                {
                    _warmAutoImportCancel = () =>
                    {
                        if (warmCtx.CancellationToken.IsCancellationRequested)
                        {
                            return;
                        }

                        _logger.Logf("Cancelling auto-import warming for file {0}", changedFile.FileName());
                        warmCts.Cancel();
                    };
                }
            }

            if (warmCtx.CancellationToken.IsCancellationRequested)
            {
                warmCts.Cancel();
                return;
            }

            try
            {
                // Clone the snapshot with auto-imports using warmCtx so the expensive
                // extraction work is cancelled if a file change arrives.
                if (!newSnapshot.TryRef())
                {
                    return;
                }

                try
                {
                    var warmChange = new SnapshotChange
                    {
                        _reason = UpdateReasonRequestedLanguageServiceWithAutoImports,
                        Documents = [changedFile],
                        AutoImports = changedFile,
                    };
                    var clonedSnapshot = newSnapshot.Clone(warmCtx, warmChange, newSnapshot.Overlays(), _logger, _client);

                    // If cancelled during clone, discard the incomplete result.
                    if (warmCtx.CancellationToken.IsCancellationRequested)
                    {
                        clonedSnapshot.Deref();
                        return;
                    }

                    // Conditionally adopt: if the session hasn't moved past newSnapshot,
                    // promote the clone so future requests benefit from the warmed cache.
                    AdoptSnapshotChange(newSnapshot, clonedSnapshot);
                }
                finally
                {
                    newSnapshot.Deref();
                }
            }
            finally
            {
                warmCts.Cancel();
            }
        }
    }

    /// <summary>Go 的 <c>fmt</c> 对切片的 <c>%v</c> 输出（`[a b c]`，nil → `[]`）：日志文本复刻，
    /// 见文件头第 8 条。</summary>
    internal static string FormatGoSlice<T>(IEnumerable<T>? values)
    {
        return "[" + string.Join(" ", (values ?? []).Select(static v => v?.ToString() ?? "<nil>")) + "]";
    }
}

                   