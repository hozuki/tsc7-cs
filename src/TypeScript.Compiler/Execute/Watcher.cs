// ported from: internal/execute/watcher.go
//
// 与 Go 的偏离（docs/architecture.md §2/§3）：
// 1) Go 未导出类型 `cachedSourceFile`/`watchCompilerHost` → `internal sealed class CachedSourceFile`/`WatchCompilerHost`
//    （§2：未导出类型按 C# 习惯命名）。Go 的**嵌入**（`compiler.CompilerHost`）在 C# 里是显式实现 + 逐成员转发：
//    `WatchCompilerHost : CompilerHost` 把除被覆写的 `GetSourceFile` 之外的全部成员转发给内嵌宿主 `_inner`
//    （也就是 Go 的嵌入字段），命名与前例 `CompilerHostImpl` 一致。
// 2) Go 未导出字段 → `internal` + `_camelCase`（`_sys`/`_config`/…）：`CreateWatcher` 是同程序集 `Execute` 静态类的成员，
//    要构造该对象，所以字段不能是 `private`（§2 的跨类 `internal` 约定）。
// 3) `(T, bool)` → §3.2 的映射：`sys.GetEnvironmentVariable(name) (string, bool)` → `sys.TryGetEnvironmentVariable(name, out value)`；
//    `SyncMap.Load` 的 `(V, bool)` → C# 的元组；`_seenFiles` 保留 `Set<Path>?`，调用点写 `?.Has(p) ?? false`
//    （Go 的 `Set.Has` 对 nil 接收者安全、返回 false，见 collections/set.go:17-22 —— C# 的 `null` 正是 Go 的 nil set）。
// 4) `collections.SyncMap`/`Set` 在 C# 是引用类型：Go 的 `&collections.SyncMap[...]{}` → `new SyncMap<...>()`；
//    "整表替换"（overflow / 配置变化）同样是整体赋一个新实例。
// 5) `reflect.DeepEqual(w.config.ParsedConfig, configParseResult.ParsedConfig)` → §3.6 禁反射 → `EqualParsedOptions`：
//    CompilerOptions 复用已有的显式字段表 `CompilerOptionFields`（它的 `Changed` 函数就是逐字段 `!reflect.DeepEqual`，
//    见 TsOptions/CompilerOptionFields.cs 文件头），其余字段（WatchOptions/TypeAcquisition/FileNames/ProjectReferences/
//    ContentMappers）按 Go 的结构逐个写死比较（文件末尾的辅助函数）。Go 的 `ParsedConfig` 是 `*ParsedOptions`（非 nil）。
// 6) `slices.Sort` + `slices.Compact`（去掉**相邻**重复）在 C# 没有等价物 → 就地展开（`List.Sort(Ordinal)` + 相邻去重循环）；
//    `slices.Equal([]string, []string)` → `SameStringArray`（元素逐个比较）。
// 7) Go 里可能为 nil 的 map/slice 返回值 → 判空或 `?? []`：`Config.ContentMappers()`/`WildcardDirectories()`/
//    `ExtendedSourceFiles()`（`string[]?`）、`wm.DrainEvents` 返回的 `changedPaths`（nil map 的 range/lookup 等价于
//    "0 次迭代 / 键不存在"）。`WildcardDirectories()` 只在 `ConfigFile != nil` 时调用（Go 侧实现依赖 ConfigFile）。
// 8) content mapper 的 error 在 ContentMapper 包里按约定**抛异常**（ContentMapper/Host.cs 文件头第 6 条；Go 的
//    `ErrProjectUnavailable` 已是 C# 异常类型）→ 用 try/catch 复刻 Go 的 `if err != nil` 分支（`WatchedFiles()`/`Refresh()`）。
// 9) `getTraceFromSys`（execute/tsc.go:283-285）属于同一个 Go 包的另一个文件 → 这里按它的函数体就地内联（`TraceFromSys`），
//    并把 `TraceFunc` 适配成 `Compiler.NewCompilerHost` 需要的 `Action<Message, object?[]>`。
// 10) 并发：Go 的 `w.wm.RunLoop(ctx, w.DoCycle)` **没有** `go` 前缀 —— 它在 `start` 里同步阻塞（非测试模式下 `start`
//    永不返回；ctx 取消后 `RunLoop` 返回，`defer` 关闭 content mapper host）。C# 照原样同步调用：`_testing == null`
//    时进循环，`_testing != null` 时不进（测试拿到 `Watcher` 后自行驱动 `DoCycle`）。注意 §3.9：`DoCycle` 会
//    bind/check/emit（深递归），生产路径必须由调用方把 `Start` 跑在显式大栈线程上。
// 11) `io.Writer` → `TextWriter`：`wm.DebugLog` 是 `TextWriter?`（Go 的 `io.Writer` 零值为 nil）；`fmt.Fprintf(w, "…%d…\n", n)`
//    → `w.Write("…" + n + "…\n")`（`Path` 覆写了 `ToString()`，与 Go 的 `%s` 一致）。
// 12) `time.Time` → `DateTime`：`FileInfo.ModTime()` 在 C# 是字段 `FileInfo.ModifiedTimeUtc`，Go 的 `t.Equal(u)`
//    是时刻比较 → C# 的 `==`（只比 ticks）。Duration/Interval 无涉及。
// 13) 本文件不 `using TypeScript.Execute.Tsc;`（那个命名空间里有接口 `System`，会遮蔽根命名空间 `System`，见
//    Execute/Tsc/Compile.cs 偏离 1）→ 静态成员走 `using static`、类型走别名 `TscNs`。同理
//    `TypeScript.Execute.WatchManager` 既是命名空间又是类名（§2 的"命名空间优先"坑）→ 类型走别名 `WatchManagerType`、
//    静态成员（Go 的包级函数）走 `using static`。
// 14) Go 的 `doBuild() error` 里唯一的 error 来源是 `reconcileWatches`（其余失败都是 panic）→ C# 落成 `bool DoBuild()`：
//    只有 `ReconcileWatches` 抛异常时打印并返回 false（其它异常照常上抛，对应 Go 的 panic），调用点据此 `ForceOverflow()`。
// 15) `Watcher` 实现 `tsc.Watcher`（Go 的 `var _ tsc.Watcher = (*Watcher)(nil)`）→ `: TscNs.Watcher`；`fswatch.Watcher`
//    与本类同名，故本文件不 `using TypeScript.FsWatch;`，只别名 `EventKind`。
// 16) Go 的 `w.config.ConfigFile` 是 `*TsConfigSourceFile` → C# 的 `TsConfigSourceFile?`（`using static TsOptions.TsOptions;`
//    会导入这个嵌套类型）。
// 17) Go 的包级函数 `createWatcher` → 与包同名的静态类 `Execute` 的成员（§2）：本文件末尾的
//    `public static partial class Execute`（与 Execute/Tsc.cs 的同一个 partial 类型），可见性 `internal`（Go 未导出）。
// 18) C# 的名字解析细节（Go 里不会遇到的问题）：`ComparePathsOptions()` 方法名与类型名 `TSPath.ComparePathsOptions` 同名
//    → 该类型用别名 `ComparePathsOptions` 引入（本文件不 `using TypeScript.TSPath;`，以免 `Path` 撞 `System.IO.Path`）；
//    `doBuild` 里快速路径的局部 `errorCount` 与之后的方法级 `errorCount` 重名 → 前者改名 `fastPathErrorCount`（CS0136）。
namespace TypeScript.Execute;

using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.TsOptions;
using TypeScript.Vfs;
using static TypeScript.Ast.Ast;
using static TypeScript.Collections.Collections;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.Execute.Incremental.Incremental;
using static TypeScript.Execute.Tsc.Tsc;
using static TypeScript.Execute.WatchManager.WatchManager;
using static TypeScript.TSPath.TSPath;
using static TypeScript.TsOptions.TsOptions;
using static TypeScript.Vfs.CachedVfs.CachedVfs;
using Comp = TypeScript.Compiler.Compiler;
using ComparePathsOptions = TypeScript.TSPath.ComparePathsOptions;
using CmHost = TypeScript.ContentMapper.Host;
using CmMapper = TypeScript.ContentMapper.Mapper;
using CmProject = TypeScript.ContentMapper.Project;
using CmProjectSpec = TypeScript.ContentMapper.ProjectSpec;
using CmSourceFiles = TypeScript.ContentMapper.SourceFiles;
using CommandLineTestingWithWatchBackend = TypeScript.Execute.WatchManager.CommandLineTestingWithWatchBackend;
using EventKind = TypeScript.FsWatch.EventKind;
using IncProgram = TypeScript.Execute.Incremental.Program;
using Path = TypeScript.TSPath.Path;
using TrackingVfsFs = TypeScript.Vfs.TrackingVfs.FS;
using TscNs = TypeScript.Execute.Tsc;
using WatchManagerType = TypeScript.Execute.WatchManager.WatchManager;

/// <summary>Go: <c>type cachedSourceFile struct</c>（watcher.go:24-27）。</summary>
internal sealed class CachedSourceFile
{
    /// <summary>Go: <c>file *ast.SourceFile</c>。</summary>
    internal SourceFile File = null!;

    /// <summary>Go: <c>modTime time.Time</c>。</summary>
    internal DateTime ModTime;
}

/// <summary>
/// Go: <c>type watchCompilerHost struct { compiler.CompilerHost; cache … }</c>（watcher.go:29-32）。
/// Go 的嵌入在 C# 里必须显式实现接口并逐成员转发（同 <c>CompilerHostImpl</c> 的写法）。
/// </summary>
internal sealed class WatchCompilerHost : CompilerHost
{
    /// <summary>Go 的嵌入字段 <c>compiler.CompilerHost</c>。</summary>
    internal CompilerHost _inner = null!;

    /// <summary>Go: <c>cache *collections.SyncMap[tspath.Path, *cachedSourceFile]</c>。</summary>
    internal SyncMap<Path, CachedSourceFile> _cache = null!;

    /// <summary>Go: <c>func (h *watchCompilerHost) GetSourceFile(opts ast.SourceFileParseOptions) *ast.SourceFile</c>（watcher.go:34-55）。</summary>
    public SourceFile? GetSourceFile(SourceFileParseOptions opts)
    {
        var info = _inner.FS().Stat(opts.FileName);

        var (cached, ok) = _cache.Load(opts.Path);
        if (ok)
        {
            if (info is not null && info.ModifiedTimeUtc == cached!.ModTime)
            {
                return cached.File;
            }
        }

        var file = _inner.GetSourceFile(opts);
        if (file is not null)
        {
            if (info is not null)
            {
                _cache.Store(opts.Path, new CachedSourceFile
                {
                    File = file,
                    ModTime = info.ModifiedTimeUtc,
                });
            }
        }
        else
        {
            _cache.Delete(opts.Path);
        }
        return file;
    }

    // 以下全部是 Go 的嵌入提升（promoted methods）在 C# 里的显式转发。

    public FS FS() => _inner.FS();

    public string DefaultLibraryPath() => _inner.DefaultLibraryPath();

    public string GetCurrentDirectory() => _inner.GetCurrentDirectory();

    public void Trace(Message msg, params object?[] args) => _inner.Trace(msg, args);

    public CmSourceFiles GetContentMappedSourceFiles(SourceFileParseOptions parseOptions, CmMapper mapper)
        => _inner.GetContentMappedSourceFiles(parseOptions, mapper);

    public CmProject? ContentMapperProject() => _inner.ContentMapperProject();

    public ParsedCommandLine? GetResolvedProjectReference(string fileName, Path path)
        => _inner.GetResolvedProjectReference(fileName, path);
}

/// <summary>Go: <c>type Watcher struct</c>（watcher.go:57-106）。</summary>
public sealed class Watcher : TscNs.Watcher
{
    internal TscNs.System _sys = null!;
    internal string _configFileName = "";
    internal ParsedCommandLine _config = null!;
    internal CompilerOptions? _compilerOptionsFromCommandLine;
    internal OrderedMap<string, object?>? _commandLineRaw;
    internal TscNs.DiagnosticReporter _reportDiagnostic = null!;
    internal TscNs.DiagnosticsReporter _reportErrorSummary = null!;
    internal TscNs.DiagnosticReporter _reportWatchStatus = null!;
    internal TscNs.CommandLineTesting? _testing;

    // contentMapperHost transforms content-mapped files; it is created once per watch session (when
    // enabled) and reused across cycles. It closes itself when the session context is cancelled (see
    // contentmapper.New).
    internal CmHost? _contentMapperHost;
    internal CmProject? _contentMapperProject;

    internal IncProgram? _program;
    internal TscNs.ExtendedConfigCache? _extendedConfigCache;
    internal bool _configModified;
    internal bool _configHasErrors;
    internal string[] _configFilePaths = [];

    internal SyncMap<Path, CachedSourceFile> _sourceFileCache = new();

    internal WatchManagerType _wm = null!;
    /// <summary>Go: <c>seenFiles *collections.Set[tspath.Path]</c>（第一次 build 之前为 nil = Go 的空集合）。</summary>
    internal Set<Path>? _seenFiles;
    /// <summary>Go: <c>configMtimes map[string]time.Time</c>（第一次 build 之前为 nil = 空 map）。</summary>
    internal Dictionary<string, DateTime>? _configMtimes;
    internal bool _watchSetDirty;
    /// <summary>
    /// Go: <c>forceFullRebuild</c>。记录"必须整表重建 NewProgram"的理由（事件溢出、周期内 watch 失败、
    /// 新出现的项目文件、变化的非源依赖）。与只用来重查通配根、确认文件集合未变后即可清除的
    /// <c>watchSetDirty</c> 不同，本标志一直保留到真正跑完一次整表重建，所以单文件快路径不可能悄悄复用旧程序。
    /// </summary>
    internal bool _forceFullRebuild;
    internal bool _programReady;

    // Test-only observability of which build path was taken.
    internal int _fastPathBuilds;
    internal int _fullBuilds;

    /// <summary>
    /// Go: <c>func (w *Watcher) start(ctx context.Context)</c>（watcher.go:142-170）。
    /// 非测试模式下本方法**阻塞**在 <c>RunLoop</c> 直到 ctx 取消（与 Go 一致，见文件头偏离 10）。
    /// </summary>
    internal void Start(Context ctx)
    {
        _contentMapperHost = NewContentMapperHost(ctx, _sys, _config.CompilerOptions()!);
        var closeContentMapperHost = _contentMapperHost is not null && _testing is null;
        try
        {
            ReplaceContentMapperProject(_config);
            _wm.Lock();
            try
            {
                _extendedConfigCache = new TscNs.ExtendedConfigCache();
                var host = Comp.NewCompilerHost(
                    _sys.GetCurrentDirectory(),
                    _sys.FS(),
                    _sys.DefaultLibraryPath(),
                    _extendedConfigCache,
                    TraceFromSys(),
                    _contentMapperProject);
                _program = ReadBuildInfoProgram(_config, NewBuildInfoReader(host), host);

                if (_configFileName != "")
                {
                    _configFilePaths = [_configFileName, .. _config.ExtendedSourceFiles() ?? []];
                }

                _sys.TryGetEnvironmentVariable("TS_WATCH_DEBUG", out var value);
                if (value != "")
                {
                    _wm.DebugLog = _sys.Writer();
                }

                if (_testing is null)
                {
                    _wm.EnsureDefaultBackend();
                }

                _reportWatchStatus(NewCompilerDiagnostic(Starting_compilation_in_watch_mode));
                _watchSetDirty = true;
                if (!DoBuild())
                {
                    _wm.ForceOverflow();
                }
            }
            finally
            {
                _wm.Unlock();
            }

            if (_testing is null)
            {
                // The content mapper host closes itself when ctx is cancelled (see contentmapper.New).
                _wm.RunLoop(ctx, DoCycle);
            }
        }
        finally
        {
            if (closeContentMapperHost)
            {
                _contentMapperHost!.Close();
            }
        }
    }

    /// <summary>Go: <c>func (w *Watcher) replaceContentMapperProject(config *tsoptions.ParsedCommandLine)</c>（watcher.go:172-185）。</summary>
    private void ReplaceContentMapperProject(ParsedCommandLine config)
    {
        if (_contentMapperHost is null)
        {
            return;
        }
        var project = _contentMapperHost.Project(new CmProjectSpec
        {
            ConfigFileName = config.ConfigName(),
            Mappers = config.ContentMappers() ?? [],
            CompilerOptions = config.CompilerOptions(),
        });
        if (_contentMapperProject is not null)
        {
            try
            {
                // Go: _ = w.contentMapperProject.Close()（显式忽略错误）
                _contentMapperProject.Close();
            }
            catch (Exception)
            {
            }
        }
        _contentMapperProject = project;
    }

    /// <summary>Go: <c>func (w *Watcher) contentMapperWatchedFiles() []string</c>（watcher.go:187-204）。</summary>
    private string[] ContentMapperWatchedFiles()
    {
        var files = new List<string>();
        foreach (var mapper in _config.ContentMappers() ?? [])
        {
            if (mapper.PackageDirectory != "" && mapper.ContributionID == "")
            {
                files.Add(CombinePaths(mapper.PackageDirectory, "package.json"));
            }
        }
        if (_contentMapperProject is not null)
        {
            string[] dynamicFiles;
            try
            {
                dynamicFiles = _contentMapperProject.WatchedFiles();
            }
            catch (Exception err)
            {
                _reportDiagnostic(Comp.ContentMapperProjectDiagnostic(err));
                return [.. files];
            }
            files.AddRange(dynamicFiles);
        }
        files.Sort(StringComparer.Ordinal);
        return Compact(files);
    }

    /// <summary>Go: <c>func (w *Watcher) computeDesiredWatches(seenFilePaths []string) map[string]bool</c>（watcher.go:206-253）。</summary>
    private Dictionary<string, bool> ComputeDesiredWatches(string[] seenFilePaths)
    {
        var cwd = _sys.GetCurrentDirectory();

        var desiredDirs = new Dictionary<string, bool>(); // dir → recursive

        // Wildcard directories from tsconfig (recursive or non-recursive)
        if (_config.ConfigFile is not null)
        {
            var wildcardDirectories = _config.WildcardDirectories();
            if (wildcardDirectories is not null)
            {
                foreach (var (dir, recursive) in wildcardDirectories)
                {
                    var realDir = _sys.FS().Realpath(dir);
                    desiredDirs[realDir] = recursive;
                }
            }
        }

        // For no-config CLI mode, ensure CWD is watched
        if (_config.ConfigFile is null && desiredDirs.Count == 0)
        {
            var dir = _sys.FS().Realpath(cwd);
            desiredDirs[dir] = false;
        }

        // Config file parent directories as non-recursive watches
        foreach (var cfgPath in _configFilePaths)
        {
            var realPath = _sys.FS().Realpath(cfgPath);
            var dir = GetDirectoryPath(realPath);
            if (!desiredDirs.ContainsKey(dir))
            {
                desiredDirs[dir] = false;
            }
        }

        // For no-config CLI mode, also watch the CLI-specified files' directories
        if (_config.ConfigFile is null)
        {
            foreach (var fileName in _config.FileNames())
            {
                var absPath = GetNormalizedAbsolutePath(fileName, cwd);
                var realPath = _sys.FS().Realpath(absPath);
                var dir = GetDirectoryPath(realPath);
                if (!desiredDirs.ContainsKey(dir))
                {
                    desiredDirs[dir] = false;
                }
            }
        }

        // Add parent directories for seen files not covered by existing dir watches.
        // Resolve ancestor fallbacks first so coverage checks use final dirs.
        var resolvedDirs = _wm.ResolveDesiredDirs(desiredDirs);

        var coverage = NewDirWatchSet(ComparePathsOptions());
        foreach (var (dir, recursive) in resolvedDirs)
        {
            coverage.Set(dir, recursive);
        }
        foreach (var filePath in seenFilePaths)
        {
            var dir = GetDirectoryPath(filePath);
            if (!coverage.Covered(dir) && CanWatchDirectory(dir))
            {
                coverage.Set(dir, false);
            }
        }

        // Re-resolve in case newly added dirs don't exist
        return _wm.ResolveDesiredDirs(coverage.Dirs());
    }

    /// <summary>Go: <c>func (w *Watcher) reconcileWatches(seenFilePaths []string) error</c>（watcher.go:255-258）。</summary>
    private void ReconcileWatches(string[] seenFilePaths)
    {
        var desiredDirs = ComputeDesiredWatches(seenFilePaths);
        _wm.ReconcileWatches(desiredDirs);
    }

    /// <summary>Go: <c>func (w *Watcher) comparePathsOptions() tspath.ComparePathsOptions</c>（watcher.go:260-265）。</summary>
    private ComparePathsOptions ComparePathsOptions() => new(
        useCaseSensitiveFileNames: _sys.FS().UseCaseSensitiveFileNames(),
        currentDirectory: _sys.GetCurrentDirectory());

    /// <summary>Go: <c>func (w *Watcher) DoCycle()</c>（watcher.go:267-351）。</summary>
    public void DoCycle()
    {
        _wm.Lock();
        try
        {
            _wm.DrainEvents(out var changedPaths, out var overflow);
            var hasEvents = changedPaths is { Count: > 0 } || overflow;

            if (RecheckTsConfig(ContentMapperManifestChanged(changedPaths)))
            {
                return;
            }

            if (hasEvents && !overflow && !_configModified)
            {
                // Filter fswatch events against known dependencies
                if (IsRelevantChange(changedPaths))
                {
                    EvictChangedSourceFiles(changedPaths);
                    var caseSensitive = _sys.FS().UseCaseSensitiveFileNames();
                    var cwd = _sys.GetCurrentDirectory();
                    var programFiles = _program!.GetProgram().FilesByPath();
                    var contentMapperWatchedFiles = NewSetFromItems(Map(ContentMapperWatchedFiles(), fileName =>
                        ToPath(fileName, cwd, caseSensitive)));
                    var contentMapperConfigChanged = false;
                    if (changedPaths is not null)
                    {
                        foreach (var eventPath in changedPaths.Keys)
                        {
                            if (_sys.FS().DirectoryExists(eventPath))
                            {
                                // A watched directory changed: the wildcard file set may have
                                // changed, so reload file names on the next build.
                                _watchSetDirty = true;
                                continue;
                            }
                            var p = ToPath(eventPath, cwd, caseSensitive);
                            if (contentMapperWatchedFiles.Has(p))
                            {
                                contentMapperConfigChanged = true;
                                _forceFullRebuild = true;
                            }
                            if (_config.ConfigFile is not null && _config.PossiblyMatchesFileName(eventPath))
                            {
                                if (!(_seenFiles?.Has(p) ?? false))
                                {
                                    // A file that matches the project but was not previously
                                    // seen appeared: a structural change that requires a full
                                    // rebuild, not the single-file fast path.
                                    _watchSetDirty = true;
                                    _forceFullRebuild = true;
                                    continue;
                                }
                            }
                            var sourceFile = programFiles.GetValueOrDefault(p);
                            if (sourceFile is not null && sourceFile.ContentMapper() != "")
                            {
                                // Canonical mapped files must be transformed again, and supplemental paths are failed
                                // physical lookups reserved for virtual files. Neither can use single-file AST reuse.
                                _forceFullRebuild = true;
                            }
                            else if (sourceFile is null && (_seenFiles?.Has(p) ?? false))
                            {
                                // A non-source build dependency changed. Such dependencies
                                // (e.g. package.json or a previously-missing module path) are
                                // tracked in seenFiles but are not program source files, so a
                                // missing sourceFileCache entry would not account for them.
                                // Module resolution may now differ, so the single-file fast
                                // path is unsafe; force a full rebuild.
                                _forceFullRebuild = true;
                            }
                        }
                    }
                    if (contentMapperConfigChanged && _contentMapperProject is not null)
                    {
                        try
                        {
                            _contentMapperProject.Refresh();
                        }
                        catch (Exception)
                        {
                            _reportDiagnostic(NewCompilerDiagnostic(The_content_mapper_process_could_not_be_started_or_initialized));
                            return;
                        }
                    }
                }
                else
                {
                    if (_wm.DebugLog is not null)
                    {
                        _wm.DebugLog.Write("[watch] DoCycle: " + (changedPaths?.Count ?? 0) + " event(s) not relevant to compilation, skipping rebuild\n");
                    }
                    if (_testing is not null)
                    {
                        _testing.OnProgram(_program!);
                    }
                    return;
                }
            }
            else if (overflow)
            {
                // Overflow: evict the entire source file cache and force a full rebuild.
                // The fast path must not run here: after clearing the cache a one-file
                // program would present exactly one cache miss and be misread as a
                // single-file content edit, silently reusing a stale (e.g. unresolved
                // import) program instead of rediscovering the file graph.
                _sourceFileCache = new SyncMap<Path, CachedSourceFile>();
                _watchSetDirty = true;
                _forceFullRebuild = true;
            }
            else if (!hasEvents && !_configModified)
            {
                // No events and no config change
                if (_wm.DebugLog is not null)
                {
                    _wm.DebugLog.Write("[watch] DoCycle: no events, skipping\n");
                }
                if (_testing is not null)
                {
                    _testing.OnProgram(_program!);
                }
                return;
            }

            _reportWatchStatus(NewCompilerDiagnostic(File_change_detected_Starting_incremental_compilation));
            if (!DoBuild())
            {
                // Mid-cycle watch failure; force a full rebuild on the next event
                _wm.ForceOverflow();
            }
        }
        finally
        {
            _wm.Unlock();
        }
    }

    /// <summary>Go: <c>func (w *Watcher) isRelevantChange(changedPaths map[string]fswatch.EventKind) bool</c>（watcher.go:353-379）。</summary>
    private bool IsRelevantChange(Dictionary<string, EventKind>? changedPaths)
    {
        var caseSensitive = _sys.FS().UseCaseSensitiveFileNames();
        var cwd = _sys.GetCurrentDirectory();
        var opts = ComparePathsOptions();
        var contentMapperWatchedFiles = NewSetFromItems(Map(ContentMapperWatchedFiles(), fileName =>
            ToPath(fileName, cwd, caseSensitive)));
        if (changedPaths is null)
        {
            return false;
        }
        foreach (var eventPath in changedPaths.Keys)
        {
            var p = ToPath(eventPath, cwd, caseSensitive);
            if (contentMapperWatchedFiles.Has(p))
            {
                return true;
            }
            if (_seenFiles?.Has(p) ?? false)
            {
                return true;
            }
            if (_config.ConfigFile is not null && _config.PossiblyMatchesFileName(eventPath))
            {
                return true;
            }
            if (_config.ConfigFile is not null && _config.PossiblyMatchesDirectoryName(p))
            {
                return true;
            }
            if (_sys.FS().DirectoryExists(eventPath))
            {
                if (_wm.IsPathUnderWatch(eventPath, opts))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Go: <c>func (w *Watcher) doBuild() error</c>（watcher.go:381-495）。
    /// Go 的 <c>error</c> 落成 <c>bool</c>（唯一的错误来源是 <c>reconcileWatches</c>，见文件头偏离 14）。
    /// </summary>
    private bool DoBuild()
    {
        if (_configModified)
        {
            _sourceFileCache = new SyncMap<Path, CachedSourceFile>();
            _watchSetDirty = true;
        }

        var reloadedFileNames = false;
        if (_watchSetDirty)
        {
            if (_config.ConfigFile is not null && (_config.WildcardDirectories()?.Count ?? 0) > 0)
            {
                var newConfig = _config.ReloadFileNamesOfParsedCommandLine(_sys.FS());
                reloadedFileNames = true;
                if (!SameStringArray(_config.FileNames(), newConfig.FileNames()))
                {
                    _config = newConfig;
                }
                else
                {
                    _watchSetDirty = false;
                    _config = newConfig;
                }
            }
            else if (!_configModified)
            {
                _watchSetDirty = false;
            }
        }

        if (_program is not null && _programReady && !_configModified && !_watchSetDirty && !_forceFullRebuild)
        {
            var cached = From(_sys.FS());
            var innerHost = Comp.NewCompilerHost(
                _sys.GetCurrentDirectory(),
                cached,
                _sys.DefaultLibraryPath(),
                _extendedConfigCache,
                TraceFromSys(),
                _contentMapperProject);
            var host = new WatchCompilerHost { _inner = innerHost, _cache = _sourceFileCache };

            if (TryUpdateProgram(host))
            {
                _fastPathBuilds++;
                var result = CompileAndEmit();
                cached.DisableAndClearCache();

                _configMtimes = new Dictionary<string, DateTime>(_configFilePaths.Length);
                foreach (var cfgPath in _configFilePaths)
                {
                    var s = _sys.FS().Stat(cfgPath);
                    if (s is not null)
                    {
                        _configMtimes[cfgPath] = s.ModifiedTimeUtc;
                    }
                }
                _configModified = false;

                // Go 里这个局部也叫 errorCount；C# 不允许内层块与之后的方法级局部重名（CS0136），改名。
                var fastPathErrorCount = result.Diagnostics.Length;
                if (fastPathErrorCount == 1)
                {
                    _reportWatchStatus(NewCompilerDiagnostic(Found_1_error_Watching_for_file_changes));
                }
                else
                {
                    _reportWatchStatus(NewCompilerDiagnostic(Found_0_errors_Watching_for_file_changes, fastPathErrorCount));
                }
                if (_testing is not null)
                {
                    _testing.OnProgram(_program!);
                }
                return true;
            }
            cached.DisableAndClearCache();
        }

        var cachedFiles = From(_sys.FS());
        var tfs = new TrackingVfsFs(cachedFiles);
        var fullInnerHost = Comp.NewCompilerHost(
            _sys.GetCurrentDirectory(),
            tfs,
            _sys.DefaultLibraryPath(),
            _extendedConfigCache,
            TraceFromSys(),
            _contentMapperProject);
        var fullHost = new WatchCompilerHost { _inner = fullInnerHost, _cache = _sourceFileCache };

        if (_config.ConfigFile is not null)
        {
            var wildcardDirectories = _config.WildcardDirectories();
            if (wildcardDirectories is not null)
            {
                foreach (var dir in wildcardDirectories.Keys)
                {
                    tfs.SeenFiles.Add(dir);
                }
            }
            if (!reloadedFileNames && !_watchSetDirty && (_config.WildcardDirectories()?.Count ?? 0) > 0)
            {
                _config = _config.ReloadFileNamesOfParsedCommandLine(_sys.FS());
            }
        }
        foreach (var path in _configFilePaths)
        {
            tfs.SeenFiles.Add(path);
        }
        foreach (var path in ContentMapperWatchedFiles())
        {
            tfs.SeenFiles.Add(path);
        }

        _program = IncProgram.NewProgram(
            Comp.NewProgram(new ProgramOptions
            {
                Config = _config,
                Host = fullHost,
            }),
            _program,
            null!,
            _sys.Now,
            _testing is not null);
        _programReady = true;
        _fullBuilds++;

        var buildResult = CompileAndEmit();
        cachedFiles.DisableAndClearCache();

        var caseSensitive = _sys.FS().UseCaseSensitiveFileNames();
        var cwd = _sys.GetCurrentDirectory();
        var seenSlice = tfs.SeenFiles.ToSlice();
        _seenFiles = NewSetWithSizeHint<Path>(seenSlice.Length);
        foreach (var p in seenSlice)
        {
            _seenFiles.Add(ToPath(p, cwd, caseSensitive));
        }

        _configMtimes = new Dictionary<string, DateTime>(_configFilePaths.Length);
        foreach (var cfgPath in _configFilePaths)
        {
            var s = _sys.FS().Stat(cfgPath);
            if (s is not null)
            {
                _configMtimes[cfgPath] = s.ModifiedTimeUtc;
            }
        }

        try
        {
            ReconcileWatches(seenSlice);
        }
        catch (Exception err)
        {
            // Go: fmt.Fprintf(w.sys.Writer(), "%v\n", err)（%v 取 error 的 Error() 文本）
            _sys.Writer().WriteLine(err.Message);
            return false;
        }
        _watchSetDirty = false;
        _configModified = false;
        _forceFullRebuild = false;

        var programFiles = _program.GetProgram().FilesByPath();
        _sourceFileCache.Range((path, _) =>
        {
            if (!programFiles.ContainsKey(path))
            {
                _sourceFileCache.Delete(path);
            }
            return true;
        });

        var errorCount = buildResult.Diagnostics.Length;
        if (errorCount == 1)
        {
            _reportWatchStatus(NewCompilerDiagnostic(Found_1_error_Watching_for_file_changes));
        }
        else
        {
            _reportWatchStatus(NewCompilerDiagnostic(Found_0_errors_Watching_for_file_changes, errorCount));
        }

        if (_testing is not null)
        {
            _testing.OnProgram(_program);
        }
        return true;
    }

    /// <summary>Go: <c>func (w *Watcher) tryUpdateProgram(host *watchCompilerHost) bool</c>（watcher.go:497-525）。</summary>
    private bool TryUpdateProgram(WatchCompilerHost host)
    {
        var oldProgram = _program!.GetProgram();

        var changedPath = default(Path);
        var changedCount = 0;
        foreach (var (path, file) in oldProgram.FilesByPath())
        {
            if (file.ContentMapper() != "")
            {
                continue;
            }
            var (_, ok) = _sourceFileCache.Load(path);
            if (!ok)
            {
                changedPath = path;
                changedCount++;
                if (changedCount > 1)
                {
                    return false;
                }
            }
        }
        if (changedCount == 0)
        {
            return false;
        }

        var oldFile = oldProgram.FilesByPath().GetValueOrDefault(changedPath);
        if (oldFile is not null)
        {
            var newFile = host.GetSourceFile(oldFile.ParseOptions());
            if (newFile is not null)
            {
                if (!EqualJSXImplicitImport(oldProgram.Options(), oldFile, newFile))
                {
                    return false;
                }
            }
        }

        var (newProgram, _, reused) = oldProgram.ReuseProgram(changedPath, host, null, null);
        if (reused)
        {
            _program = IncProgram.NewProgram(newProgram!, _program, null!, _sys.Now, _testing is not null);
        }
        return reused;
    }

    /// <summary>
    /// FastPathBuilds reports how many builds reused an existing program via the
    /// UpdateProgram single-file fast path. It is intended for tests that need to
    /// verify which build path was taken.
    /// </summary>
    public int FastPathBuilds() => _fastPathBuilds;

    /// <summary>
    /// FullBuilds reports how many builds constructed a full program via NewProgram.
    /// It is intended for tests that need to verify which build path was taken.
    /// </summary>
    public int FullBuilds() => _fullBuilds;

    /// <summary>Go: <c>func equalJSXImplicitImport(options, oldFile, newFile) bool</c>（watcher.go:527-537）。</summary>
    private static bool EqualJSXImplicitImport(CompilerOptions? options, SourceFile oldFile, SourceFile newFile)
    {
        static bool IsJSX(SourceFile file) =>
            file.ScriptKind == ScriptKind.JSX || file.ScriptKind == ScriptKind.TSX;
        if (!IsJSX(oldFile) && !IsJSX(newFile))
        {
            return true;
        }
        var oldImport = GetJSXRuntimeImport(GetJSXImplicitImportBase(options, oldFile), options);
        var newImport = GetJSXRuntimeImport(GetJSXImplicitImportBase(options, newFile), options);
        return oldImport == newImport;
    }

    /// <summary>Go: <c>func (w *Watcher) evictChangedSourceFiles(changedPaths map[string]fswatch.EventKind)</c>（watcher.go:539-550）。</summary>
    private void EvictChangedSourceFiles(Dictionary<string, EventKind>? changedPaths)
    {
        var caseSensitive = _sys.FS().UseCaseSensitiveFileNames();
        var cwd = _sys.GetCurrentDirectory();
        if (changedPaths is null)
        {
            return;
        }
        foreach (var eventPath in changedPaths.Keys)
        {
            var p = ToPath(eventPath, cwd, caseSensitive);
            var (_, ok) = _sourceFileCache.Load(p);
            if (ok)
            {
                if (_wm.DebugLog is not null)
                {
                    _wm.DebugLog.Write("[watch] evicting cached source file: " + p + "\n");
                }
                _sourceFileCache.Delete(p);
            }
        }
    }

    /// <summary>Go: <c>func (w *Watcher) compileAndEmit() tsc.CompileAndEmitResult</c>（watcher.go:552-564）。</summary>
    private TscNs.CompileAndEmitResult CompileAndEmit()
    {
        return EmitFilesAndReportErrors(new TscNs.EmitInput
        {
            Sys = _sys,
            ProgramLike = _program!,
            Program = _program!.GetProgram(),
            Config = _config,
            ReportDiagnostic = _reportDiagnostic,
            ReportErrorSummary = _reportErrorSummary,
            Writer = _sys.Writer(),
            CompileTimes = new TscNs.CompileTimes(),
            Testing = _testing,
        });
    }

    /// <summary>Go: <c>func (w *Watcher) contentMapperManifestChanged(changedPaths map[string]fswatch.EventKind) bool</c>（watcher.go:566-576）。</summary>
    private bool ContentMapperManifestChanged(Dictionary<string, EventKind>? changedPaths)
    {
        foreach (var mapper in _config.ContentMappers() ?? [])
        {
            if (mapper.PackageDirectory == "" || mapper.ContributionID != "")
            {
                continue;
            }
            if (changedPaths?.ContainsKey(CombinePaths(mapper.PackageDirectory, "package.json")) ?? false)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Go: <c>func (w *Watcher) recheckTsConfig(force bool) bool</c>（watcher.go:578-617）。</summary>
    private bool RecheckTsConfig(bool force)
    {
        if (_configFileName == "")
        {
            return false;
        }

        if (!force && !_configHasErrors && _configFilePaths.Length > 0)
        {
            var changed = false;
            foreach (var path in _configFilePaths)
            {
                var oldMtime = default(DateTime);
                var hasOldMtime = _configMtimes is not null && _configMtimes.TryGetValue(path, out oldMtime);
                var s = _sys.FS().Stat(path);
                if (!hasOldMtime)
                {
                    if (s is not null)
                    {
                        changed = true;
                        break;
                    }
                }
                else if (s is null || s.ModifiedTimeUtc != oldMtime)
                {
                    changed = true;
                    break;
                }
            }
            if (!changed)
            {
                return false;
            }
        }

        var configParseResult = ParseConfigFile();
        if (configParseResult is null)
        {
            return true;
        }
        if (_configHasErrors)
        {
            _configModified = true;
        }
        _configHasErrors = false;
        _configFilePaths = [_configFileName, .. configParseResult.ExtendedSourceFiles() ?? []];
        if (!EqualParsedOptions(_config.ParsedConfig, configParseResult.ParsedConfig))
        {
            _configModified = true;
        }
        ReplaceContentMapperProject(configParseResult);
        _config = configParseResult;
        return false;
    }

    /// <summary>Go: <c>func (w *Watcher) parseConfigFile() *tsoptions.ParsedCommandLine</c>（watcher.go:619-641）。</summary>
    private ParsedCommandLine? ParseConfigFile()
    {
        var extendedConfigCache = new TscNs.ExtendedConfigCache();
        var (configParseResult, errors) = GetParsedCommandLineOfConfigFile(
            _configFileName,
            _compilerOptionsFromCommandLine,
            _commandLineRaw,
            _sys,
            extendedConfigCache);
        if (errors.Length > 0)
        {
            foreach (var e in errors)
            {
                _reportDiagnostic(e);
            }
            _configHasErrors = true;
            var errorCount = errors.Length;
            if (errorCount == 1)
            {
                _reportWatchStatus(NewCompilerDiagnostic(Found_1_error_Watching_for_file_changes));
            }
            else
            {
                _reportWatchStatus(NewCompilerDiagnostic(Found_0_errors_Watching_for_file_changes, errorCount));
            }
            return null;
        }
        _extendedConfigCache = extendedConfigCache;
        return configParseResult;
    }

    /// <summary>
    /// Go: <c>func getTraceFromSys(sys tsc.System, locale locale.Locale, testing tsc.CommandLineTesting) func(msg *diagnostics.Message, args ...any)</c>
    /// （execute/tsc.go:283-285）就地内联：它的函数体就是 <c>tsc.GetTraceWithWriterFromSys(sys.Writer(), locale, testing)</c>；
    /// 返回值再从 <c>TraceFunc</c> 适配成 <c>Compiler.NewCompilerHost</c> 需要的 <c>Action&lt;Message, object?[]&gt;</c>。
    /// </summary>
    private Action<Message, object?[]> TraceFromSys()
    {
        var trace = GetTraceWithWriterFromSys(_sys.Writer(), _config.Locale, _testing);
        return (msg, args) => trace(msg, args);
    }

    /// <summary>Go: <c>slices.Compact</c>：去掉**相邻**重复元素（前置条件：<paramref name="sorted"/> 已排序）。</summary>
    private static string[] Compact(List<string> sorted)
    {
        if (sorted.Count == 0)
        {
            return [];
        }
        var result = new List<string>(sorted.Count) { sorted[0] };
        for (var i = 1; i < sorted.Count; i++)
        {
            if (sorted[i] != sorted[i - 1])
            {
                result.Add(sorted[i]);
            }
        }
        return [.. result];
    }

    /// <summary>Go: <c>slices.Equal(a, b)</c>（元素逐个比较；nil 与空切片在 Go 里相等）。</summary>
    private static bool SameStringArray(string[]? a, string[]? b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }
        if (a is null || b is null)
        {
            return (a?.Length ?? 0) == 0 && (b?.Length ?? 0) == 0;
        }
        if (a.Length != b.Length)
        {
            return false;
        }

        // 内置 span 序列比较（`string` 元素 → `EqualityComparer<string>.Default`，与 `a[i] != b[i]` 同语义）。
        return a.AsSpan().SequenceEqual(b.AsSpan());
    }

    private static bool SameBytes(byte[]? a, byte[]? b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }
        if (a is null || b is null || a.Length != b.Length)
        {
            return false;
        }
        return a.AsSpan().SequenceEqual(b);
    }

    /// <summary>
    /// Go: <c>reflect.DeepEqual(w.config.ParsedConfig, configParseResult.ParsedConfig)</c>（§3.6 禁反射 → 逐字段比较）。
    /// CompilerOptions 走既有字段表（覆盖 Go 的全部导出字段，`Changed` 即逐字段 <c>!reflect.DeepEqual</c>），
    /// 其余字段按 Go 的结构展开。Go 的 DeepEqual 语义：指针递归比较目标值、slice 逐元素（nil ≠ 空 slice）。
    /// </summary>
    private static bool EqualParsedOptions(ParsedOptions a, ParsedOptions b)
    {
        // CompilerOptions（Go 的 *core.CompilerOptions）
        var optionsA = a.CompilerOptions;
        var optionsB = b.CompilerOptions;
        if (optionsA is null || optionsB is null)
        {
            if (optionsA is not null || optionsB is not null)
            {
                return false;
            }
        }
        else
        {
            foreach (var field in CompilerOptionFields)
            {
                if (field.Changed(optionsA, optionsB))
                {
                    return false;
                }
            }
        }

        // WatchOptions（Go 的 *core.WatchOptions）
        var watchA = a.WatchOptions;
        var watchB = b.WatchOptions;
        if (watchA is null || watchB is null)
        {
            if (watchA is not null || watchB is not null)
            {
                return false;
            }
        }
        else if (watchA.FileKind != watchB.FileKind
            || watchA.DirectoryKind != watchB.DirectoryKind
            || watchA.FallbackPolling != watchB.FallbackPolling
            || watchA.SyncWatchDir != watchB.SyncWatchDir
            || !object.Equals(watchA.Interval, watchB.Interval)
            || !SameStringArray(watchA.ExcludeDir, watchB.ExcludeDir)
            || !SameStringArray(watchA.ExcludeFiles, watchB.ExcludeFiles))
        {
            return false;
        }

        // TypeAcquisition（Go 已有值相等方法，含 nil 分支）
        var taA = a.TypeAcquisition;
        var taB = b.TypeAcquisition;
        if (taA is null || taB is null)
        {
            if (taA is not null || taB is not null)
            {
                return false;
            }
        }
        else if (!taA.Equals(taB))
        {
            return false;
        }

        // FileNames（Go 的 []string；C# 侧恒为非空数组，nil/空的差别见 ParsedOptions 文件头第 1 条）
        if (!SameStringArray(a.FileNames, b.FileNames))
        {
            return false;
        }

        // ProjectReferences（Go 的 []*core.ProjectReference）
        var refsA = a.ProjectReferences;
        var refsB = b.ProjectReferences;
        if (refsA is null || refsB is null)
        {
            return refsA is null && refsB is null;
        }
        if (refsA.Length != refsB.Length)
        {
            return false;
        }
        for (var i = 0; i < refsA.Length; i++)
        {
            if (refsA[i].Path != refsB[i].Path
                || refsA[i].OriginalPath != refsB[i].OriginalPath
                || refsA[i].Circular != refsB[i].Circular)
            {
                return false;
            }
        }

        // ContentMappers（Go 的 []*contentmapper.Mapper，逐字段递归）
        var mappersA = a.ContentMappers;
        var mappersB = b.ContentMappers;
        if (mappersA is null || mappersB is null)
        {
            return mappersA is null && mappersB is null;
        }
        if (mappersA.Length != mappersB.Length)
        {
            return false;
        }
        for (var i = 0; i < mappersA.Length; i++)
        {
            if (!EqualMapper(mappersA[i], mappersB[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Go: 对 <c>*contentmapper.Mapper</c> 的 DeepEqual（Definition + Manifest 的字段在 C# 里是平铺的）。</summary>
    private static bool EqualMapper(CmMapper a, CmMapper b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }
        if (a is null || b is null)
        {
            return false;
        }
        return a.Package == b.Package
            && SameStringArray(a.Extensions, b.Extensions)
            && SameBytes(a.Options, b.Options)
            && a.Name == b.Name
            && a.Version == b.Version
            && SameStringArray(a.Exec, b.Exec)
            && SameStringArray(a.CompilerOptions, b.CompilerOptions)
            && a.DynamicConfig == b.DynamicConfig
            && a.PackageDirectory == b.PackageDirectory
            && a.ContributionID == b.ContributionID;
    }
}

/// <summary>Go 包级函数（package execute）→ 与包同名的静态类（docs/architecture.md §2）。</summary>
public static partial class Execute
{
    /// <summary>Go: <c>func createWatcher(…)</c>（watcher.go:110-140）。</summary>
    internal static Watcher CreateWatcher(
        TscNs.System sys,
        ParsedCommandLine configParseResult,
        CompilerOptions? compilerOptionsFromCommandLine,
        OrderedMap<string, object?>? commandLineRaw,
        TscNs.DiagnosticReporter reportDiagnostic,
        TscNs.DiagnosticsReporter reportErrorSummary,
        TscNs.CommandLineTesting? testing)
    {
        var wm = NewWatchManager(sys.Writer(), sys.FS().DirectoryExists);
        if (testing is CommandLineTestingWithWatchBackend withBackend)
        {
            wm.SetBackend(withBackend.WatchBackend());
        }
        var w = new Watcher
        {
            _sys = sys,
            _config = configParseResult,
            _compilerOptionsFromCommandLine = compilerOptionsFromCommandLine,
            _commandLineRaw = commandLineRaw,
            _reportDiagnostic = reportDiagnostic,
            _reportErrorSummary = reportErrorSummary,
            _reportWatchStatus = CreateWatchStatusReporter(sys, configParseResult.Locale, configParseResult.CompilerOptions(), testing),
            _testing = testing,
            _sourceFileCache = new SyncMap<Path, CachedSourceFile>(),
            _wm = wm,
        };
        if (configParseResult.ConfigFile is not null)
        {
            w._configFileName = configParseResult.ConfigFile.SourceFile.FileName();
        }
        return w;
    }
}
