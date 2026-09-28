// ported from: internal/project/snapshot.go:1-806
//
// 与 Go 的偏离（逐条说明）：
//
// 1) **包级函数的落点**：Go 的 `project` 包与类型 `Project` 同名，C# 里不能再声明 `static class Project`
//    （CS0101），包级函数挂到"它构造/返回的类型"上（先例 `Locale.Parse`、`Project.NewConfiguredProject`）：
//      `newSnapshot`（Go 是 `SnapshotHost` 的方法）→ `SnapshotHost.NewSnapshot`（见 SnapshotHost.cs）；
//      `snapshotOverlays(fs)`（未导出）→ `Snapshot.SnapshotOverlays(SnapshotFS)`；
//      `overlayFileHandles(overlays)`（未导出）→ `Snapshot.OverlayFileHandles(Dictionary<Path, Overlay>)`。
//
// 2) **Go 的 `sync.Once`**（`contentMapperWatchStateOnce`）→ **标志 + `System.Threading.Lock` 双检**
//    （本仓约定，见 Project.cs 文件头第 4 条）：读路径先看标志（`Volatile.Read`），未完成才进锁。
//
// 3) **Go 的 `atomic.Int32`**（`refCount`）→ `int` + `Interlocked`：
//      `ref()`      → `Ref()`：`Interlocked.Increment` 后 `<= 1` 说明原本已释放 → `Debug.Fail`（Go 的 panic）；
//      `tryRef()`   → `TryRef()`：CAS 循环（`Interlocked.CompareExchange`）；
//      `Deref()`    → 同上；`dispose()`（未导出）→ `Dispose()`（internal，规则 2 的"未导出方法 → PascalCase"）。
//    注意 `Ref()`/`TryRef()`/`Dispose()` 在 Go 里是未导出方法，C# 侧为 `internal`（跨文件可用，不泄漏到公开 API）。
//
// 4) **`panic(fmt.Sprintf(...))` → `Debug.Fail(...)`**（`TypeScript.Debug`，抛 DebugFailureException）；
//    `Debug.Fail` 没有 [DoesNotReturn]，Go 里 panic 后的代码在 C# 里不可达，故照本仓先例在其后抛
//    `UnreachableException` 或直接 `return`（本文件按调用点需要处理）。
//
// 5) **Go 的 `defer + recover`**（Clone 里"克隆失败时先把已收集的日志打出来再重抛"）→ `try/catch when`：
//    `catch when (loggingEnabled)` 里 `sessionLogger.Log(logger.String())` 后 `throw;`（保留原始堆栈）。
//
// 6) `SnapshotChange` 在 Go 里**内嵌** `ResourceRequest`（`change.Documents` 是提升的字段）⇒ C# 用继承
//    `SnapshotChange : ResourceRequest`（对象初始化器可以直接写继承来的 public 字段）。
//    Go 的 `SnapshotChange{ResourceRequest: x}`（整块结构体复制）→ `SnapshotChange.CopyFrom(x)`（浅复制，
//    与 Go 复制结构体时切片/map 共享同一底层一致）。
//
// 7) `change.reason`（未导出字段）→ `change._reason`；`UpdateReason` 的枚举成员保留 Go 的全名
//    （`UpdateReasonDidOpenFile`…），故本文件 `using static TypeScript.Project.UpdateReason;` 后裸写
//    （与 `Kind`/`ProgramUpdateKind` 的既有做法一致）。
//
// 8) `*T` → `T?`、nil 切片 → `T[]?` + `?? []`、`map[K]V` → `Dictionary<K,V>`。
//    `(T,bool)` 元组：`SyncMapEntry`/`Finalize` 等已有本仓形状，直接按其字段名解构
//    （`fs.Finalize()` → `(SnapshotFS Snapshot, bool Changed)`）。
//
// 9) `time.Now()`/`time.Since` → `Stopwatch.GetTimestamp()`/`Stopwatch.GetElapsedTime`（本仓约定，
//    见 LsAutoImport/Registry.Build.cs 文件头第 9 条）；`Logger.Logf` 用 C# 复合格式。
//    `logger.Logf(...)` 在 Go 里可以作用在 nil `*LogTree` 上（nil 接收者空操作）⇒ C# 写 `logger?.Logf(...)`。
//
// 10) `newAutoImportRegistryCloneHost`（`project/autoimport.go` 的包级未导出函数）落在本包的
//     `Project/AutoImport.cs` → `AutoImport.NewAutoImportRegistryCloneHost(...)`（本切片自己实现，见该文件头）。
//
// 11) `tspath.Path` → `Path`（`using Path = TypeScript.TSPath.Path;`）；`lsproto.DocumentUri` 的
//     `uri.Path(useCaseSensitive)` 保留。`vfsmatch.ReadDirectory` → `VfsMatch.ReadDirectory`（静态类）。
//
// 12) `AutoImports.Clone(...)` 在 Go 里返回 `(*Registry, error)`，`autoimport` 切片的落地是"永不返回错误、
//     失败抛异常"（M8LsAutoImport 的既定形状）⇒ 这里没有 `err` 分支，`if err == nil` 直接消失
//     （Go 里该分支恒真）。`s.autoImportsWatch.Clone(...)` 同样按非空处理（根快照总会建它）。
using TypeScript.Collections;
using TypeScript.Compiler;
using TypeScript.ContentMapper;
using TypeScript.Core;
using TypeScript.LsAutoImport;
using TypeScript.LsUtil;
using TypeScript.Project.Logging;
using TypeScript.TSPath;
using TypeScript.Vfs;
using TypeScript.Vfs.VfsMatch;
using static TypeScript.Core.Core;
using static TypeScript.Project.Kind;
using static TypeScript.Project.Logging.Logging;
using static TypeScript.Project.ProgramUpdateKind;
using static TypeScript.Project.UpdateReason;
using Path = TypeScript.TSPath.Path;
using Stopwatch = System.Diagnostics.Stopwatch;
using Logger = TypeScript.Project.Logging.Logger;

namespace TypeScript.Project;

/// <summary>Go 的 <c>Snapshot</c>（snapshot.go:27-60）：所有项目的不可变状态 + 它的引用计数。
/// 基类列表里的 <c>TypeScript.Module.ResolutionHost</c> 是**非 Go 源的必要写法**：Go 的 `*project.Snapshot`
/// 只靠 <c>FS()</c> + <c>GetCurrentDirectory()</c> **结构性**满足 `module.ResolutionHost`
/// （`internal/api/module_resolution.go:363` 的 `Host: sd.snapshot`、以及 module 包内多处），C# 的接口是名义的，
/// 必须显式列出（先例：Compiler/Host.cs 文件头第 6 条、Execute/Tsc/Compile.cs 的 `System : Spawner, ParseConfigHost`）。</summary>
public sealed class Snapshot : Ls.Host, Module.ResolutionHost
{
    internal SnapshotHost _host = null!;

    internal ulong _id;

    internal ulong _parentId;

    /// <summary>Go: <c>refCount atomic.Int32</c>（见文件头第 3 条）。</summary>
    private int _refCount;

    internal LsConv.Converters _converters = null!;

    // Immutable state, cloned between snapshots

    internal SnapshotFS _fs = null!;

    public ProjectCollection ProjectCollection = null!;

    public ConfigFileRegistry ConfigFileRegistry = null!;

    public Registry? AutoImports;

    internal WatchedFiles<Dictionary<Path, string>>? _autoImportsWatch;

    internal CompilerOptions? _compilerOptionsForInferredProjects;

    internal Mapper[] _inferredProjectContentMappers = [];

    internal string[] _inferredProjectContentMapperExtensions = [];

    internal UserPreferences _userPreferences;

    private readonly Lock _contentMapperWatchStateLock = new();
    private bool _contentMapperWatchStateDone;
    private string[] _contentMapperExtensions = [];
    private Set<Path>? _contentMapperWatchedFiles;

    internal LogTree? _builderLogs;

    internal Exception? _apiError;

    /// <summary>fileSystemOverride indicates that this snapshot was built from a filesystem
    /// supplied by an API update rather than the session host filesystem.</summary>
    internal bool _fileSystemOverride;

    internal Project[] _createdPrograms = [];

    /// <summary>Go: <c>func (s *Snapshot) contentMapperWatchState() ([]string, *collections.Set[tspath.Path])</c>
    /// （snapshot.go:62-79，sync.Once 见文件头第 2 条）。</summary>
    internal (string[] Extensions, Set<Path>? WatchedFiles) ContentMapperWatchState()
    {
        if (!Volatile.Read(ref _contentMapperWatchStateDone))
        {
            lock (_contentMapperWatchStateLock)
            {
                if (!_contentMapperWatchStateDone)
                {
                    var configured = ConfigFileRegistry.ContentMappers();
                    if (configured is not null)
                    {
                        _contentMapperExtensions = [.. configured._extensions];
                    }

                    _contentMapperExtensions = [.. _contentMapperExtensions, .. _inferredProjectContentMapperExtensions];
                    Array.Sort(_contentMapperExtensions, StringComparer.Ordinal);
                    _contentMapperExtensions = DeduplicateSorted(_contentMapperExtensions, static (a, b) => string.Equals(a, b, StringComparison.Ordinal));

                    _contentMapperWatchedFiles = new Set<Path>();
                    foreach (var project in ProjectCollection.Projects())
                    {
                        if (project._contentMapperWatchedFiles is not null)
                        {
                            foreach (var path in project._contentMapperWatchedFiles.Keys())
                            {
                                _contentMapperWatchedFiles.Add(path);
                            }
                        }
                    }

                    Volatile.Write(ref _contentMapperWatchStateDone, true);
                }
            }
        }

        return (_contentMapperExtensions, _contentMapperWatchedFiles);
    }

    /// <summary>Go: <c>func (host *SnapshotHost) newSnapshot(...) *Snapshot</c>（snapshot.go:81-105）。</summary>
    internal static Snapshot NewSnapshot(
        SnapshotHost host,
        ulong id,
        SnapshotFS fs,
        ConfigFileRegistry? configFileRegistry,
        CompilerOptions? compilerOptionsForInferredProjects,
        UserPreferences userPreferences,
        Registry? autoImports,
        WatchedFiles<Dictionary<Path, string>>? autoImportsWatch)
    {
        var overlays = SnapshotOverlays(fs);
        var s = new Snapshot
        {
            _host = host,
            _id = id,

            _fs = fs,
            ConfigFileRegistry = configFileRegistry!,
            ProjectCollection = new ProjectCollection
            {
                _toPath = host._toPath,
                _openFiles = ProjectCollection.OpenFilePaths(overlays),
            },
            _compilerOptionsForInferredProjects = compilerOptionsForInferredProjects,
            _userPreferences = userPreferences,
            AutoImports = autoImports,
            _autoImportsWatch = autoImportsWatch,
        };
        s._refCount = 1;
        s._converters = LsConv.LsConv.NewConverters(host._options.PositionEncoding, s.LSPLineMap);
        return s;
    }

    /// <summary>Go: <c>func snapshotOverlays(fs *SnapshotFS) map[tspath.Path]*Overlay</c>（snapshot.go:107-109）。</summary>
    internal static Dictionary<Path, Overlay> SnapshotOverlays(SnapshotFS fs)
    {
        return fs._fs.Overlays();
    }

    internal Dictionary<Path, Overlay> Overlays()
    {
        return SnapshotOverlays(_fs);
    }

    public Project[] CreatedPrograms()
    {
        return _createdPrograms;
    }

    internal ResourceRequest ResourceRequestForDocument(LsProto.DocumentUri uri)
    {
        var path = uri.Path(UseCaseSensitiveFileNames());
        var request = new ResourceRequest { Documents = [uri] };
        foreach (var project in ProjectCollection.SyntheticProjects())
        {
            if (project.ContainsFile(path) || project._host is not null && project._host._sourceFS.SeenFileOrMissingParentDirectory(path))
            {
                request.Projects = [.. request.Projects ?? [], project.ID()];
            }
        }

        return request;
    }

    /// <summary>Go: <c>func (s *Snapshot) processFileChanges(...) FileChangeSummary</c>（snapshot.go:119-176）。</summary>
    internal FileChangeSummary ProcessFileChanges(
        SnapshotFSBuilder fs,
        FileChangeSummary fileChanges,
        LogTree? logger,
        ContentMapperContributions? contentMapperContributions,
        Dictionary<Path, Overlay> previousOverlays,
        Dictionary<Path, Overlay> overlays)
    {
        if (fs._fs is FileChangeExpander expander)
        {
            fileChanges = expander.ExpandFileChanges(fileChanges);
        }

        var previousOpenFiles = OverlayFileHandles(previousOverlays);
        var openFiles = OverlayFileHandles(overlays);
        if (fileChanges.HasExcessiveWatchEvents())
        {
            var invalidateStart = Stopwatch.GetTimestamp();
            if (fileChanges.InvalidateAll)
            {
                fs.InvalidateCache();
                logger?.Logf("InvalidateAll: invalidated file cache in {0}", Stopwatch.GetElapsedTime(invalidateStart));
            }
            else if (!fs.WatchChangesOverlapCache(fileChanges, previousOpenFiles, openFiles))
            {
                // All watch changes/deletes are files we haven't seen; should be irrelevant to us (probably an external tool's build or something)
                fileChanges.Changed = new Set<LsProto.DocumentUri>();
                fileChanges.Deleted = new Set<LsProto.DocumentUri>();
            }
            else if (fileChanges.IncludesWatchChangeOutsideNodeModules)
            {
                fs.InvalidateCache();
                logger?.Logf("Excessive watch changes detected, invalidated file cache in {0}", Stopwatch.GetElapsedTime(invalidateStart));
            }
            else
            {
                fs.InvalidateNodeModulesCache();
                logger?.Logf("npm install detected, invalidated node_modules cache in {0}", Stopwatch.GetElapsedTime(invalidateStart));
            }
        }
        else
        {
            string[] contentMapperExtensions;
            if (contentMapperContributions is null)
            {
                (contentMapperExtensions, _) = ContentMapperWatchState();
            }
            else
            {
                var configuredContentMappers = ConfigFileRegistry.ContentMappers();
                contentMapperExtensions = configuredContentMappers is not null ? [.. configuredContentMappers._extensions] : [];
                contentMapperExtensions = [.. contentMapperExtensions, .. contentMapperContributions.Extensions];
            }

            var (_, contentMapperWatchedFiles) = ContentMapperWatchState();
            fileChanges = fs.ExpandAndFilterWatchEvents(fileChanges, contentMapperExtensions, contentMapperWatchedFiles, previousOpenFiles, openFiles);
            fileChanges = _fs.ExpandRealpathAliases(fileChanges);
            fileChanges = fs.MarkDirtyFiles(fileChanges);
            fileChanges = fs.ConvertOpenAndCloseToChanges(fileChanges, previousOpenFiles, openFiles);
        }

        foreach (var path in openFiles.Keys)
        {
            var (entry, ok) = fs._cacheFiles.Load(path);
            if (ok)
            {
                fs.DeleteCacheEntry(entry!);
            }
        }

        return fileChanges;
    }

    /// <summary>Go: <c>func overlayFileHandles(overlays map[tspath.Path]*Overlay) map[tspath.Path]FileHandle</c>（snapshot.go:178-184）。</summary>
    internal static Dictionary<Path, FileHandle> OverlayFileHandles(Dictionary<Path, Overlay> overlays)
    {
        var files = new Dictionary<Path, FileHandle>(overlays.Count);
        foreach (var (path, overlay) in overlays)
        {
            files[path] = overlay;
        }

        return files;
    }

    public Project? GetDefaultProject(LsProto.DocumentUri uri)
    {
        return ProjectCollection.GetDefaultProject(uri.Path(UseCaseSensitiveFileNames()));
    }

    /// <summary>GetLanguageServiceProjectsContainingFile does not consider synthetic projects
    /// (ones created by API via createProgram).</summary>
    public Ls.Project[] GetLanguageServiceProjectsContainingFile(LsProto.DocumentUri uri)
    {
        var fileName = uri.FileName();
        var path = _host._toPath(fileName);
        // TODO!! sheetal may be change this to handle symlinks!!
        return ProjectCollection.GetLanguageServiceProjectsContainingFile(path);
    }

    public FileHandle? GetFile(string fileName)
    {
        return _fs.GetFile(fileName);
    }

    public LsConv.LSPLineMap? LSPLineMap(string fileName)
    {
        if (GetFile(fileName) is { } file)
        {
            return file.LSPLineMap();
        }

        return null;
    }

    public SourceMap.ECMALineInfo? GetECMALineInfo(string fileName)
    {
        if (GetFile(fileName) is { } file)
        {
            return file.ECMALineInfo();
        }

        return null;
    }

    public UserPreferences GetPreferences(string activeFile)
    {
        return _userPreferences;
    }

    public UserPreferences UserPreferences()
    {
        return _userPreferences;
    }

    public LsConv.Converters Converters()
    {
        return _converters;
    }

    public Registry AutoImportRegistry()
    {
        return AutoImports!;
    }

    public ulong ID()
    {
        return _id;
    }

    internal Path ToPath(string fileName)
    {
        return _host._toPath(fileName);
    }

    internal bool IsOpenFile(string fileName)
    {
        return Overlays().ContainsKey(ToPath(fileName));
    }

    internal bool HasOverlayWithin(Path path)
    {
        foreach (var overlayPath in Overlays().Keys)
        {
            if (path.ContainsPath(overlayPath))
            {
                return true;
            }
        }

        return false;
    }

    public bool UseCaseSensitiveFileNames()
    {
        return _fs._fs.UseCaseSensitiveFileNames();
    }

    /// <summary>FileSystem returns the filesystem backing this snapshot.</summary>
    public Vfs.FS FileSystem()
    {
        return _fs._fs;
    }

    /// <summary>HasFileSystemOverride reports whether this snapshot uses an API-supplied
    /// filesystem instead of the session host filesystem.</summary>
    public bool HasFileSystemOverride()
    {
        return _fileSystemOverride;
    }

    /// <summary>Go: <c>func (s *Snapshot) ReadFile(fileName string) (string, bool)</c>（实现 <c>ls.Host</c>）。</summary>
    public bool TryReadFile(string fileName, out string contents)
    {
        var handle = GetFile(fileName);
        if (handle is null)
        {
            contents = "";
            return false;
        }

        contents = handle.Content();
        return true;
    }

    public bool DirectoryExists(string path)
    {
        return _fs._fs.DirectoryExists(path);
    }

    public bool FileExists(string path)
    {
        return _fs._fs.FileExists(path);
    }

    public string[] GetDirectories(string path)
    {
        return _fs._fs.GetAccessibleEntries(path).Directories;
    }

    public string[] ReadDirectory(string currentDir, string path, string[]? extensions, string[]? excludes, string[]? includes, int depth)
    {
        return VfsMatch.ReadDirectory(_fs._fs, currentDir, path, extensions, excludes, includes, depth);
    }

    public Vfs.FS FS()
    {
        return SourceFS.NewSourceFS(false, _fs, _host._toPath);
    }

    public string GetCurrentDirectory()
    {
        return _host.GetCurrentDirectory();
    }

    public string[] ContentMapperExtensions()
    {
        var (extensions, _) = ContentMapperWatchState();
        return extensions;
    }

    /// <summary>Go: <c>func (s *Snapshot) Clone(ctx, change, overlays, sessionLogger, client) *Snapshot</c>
    /// （snapshot.go:441-731）。</summary>
    public Snapshot Clone(
        Context ctx,
        SnapshotChange change,
        Dictionary<Path, Overlay> overlays,
        Logger? sessionLogger,
        Client? client)
    {
        if (_apiError is not null)
        {
            // Go: panic(fmt.Sprintf("cannot clone snapshot with API error: %v", s.apiError))
            Debug.Fail($"cannot clone snapshot with API error: {_apiError}");
            throw new System.Diagnostics.UnreachableException();
        }

        var store = _host;
        LogTree? logger = null;
        var loggingEnabled = store._options.LoggingEnabled && sessionLogger is not null;
        try
        {
            // Print in-progress logs immediately if cloning fails
            if (loggingEnabled)
            {
                logger = NewLogTree($"Cloning snapshot {_id}");
                var getDetails = () =>
                {
                    var details = "";
                    if (change.Documents is { Length: > 0 })
                    {
                        details += $" Documents: {string.Join(", ", change.Documents.Select(static d => d.ToString()))}";
                    }

                    if (change.ConfiguredProjectDocuments is { Length: > 0 })
                    {
                        details += $" ConfiguredProjectDocuments: {string.Join(", ", change.ConfiguredProjectDocuments.Select(static d => d.ToString()))}";
                    }

                    if (change.Projects is { Length: > 0 })
                    {
                        details += $" Projects: {string.Join(", ", change.Projects.Select(static p => p.String()))}";
                    }

                    if (change.ProjectTree is not null)
                    {
                        details += $" ProjectTree: {string.Join(", ", (change.ProjectTree.Projects() ?? []).Select(static p => p.ToString()))}";
                    }

                    return details;
                };
                switch (change._reason)
                {
                    case UpdateReasonDidOpenFile:
                        logger.Logf("Reason: DidOpenFile - {0}", change._fileChanges.Opened.ToString());
                        break;
                    case UpdateReasonDidCloseFile:
                        logger.Logf("Reason: DidCloseFile - {0}", string.Join(", ", change._fileChanges.Closed.Keys().Select(static u => u.ToString())));
                        break;
                    case UpdateReasonDidChangeCompilerOptionsForInferredProjects:
                        logger.Logf("Reason: DidChangeCompilerOptionsForInferredProjects");
                        break;
                    case UpdateReasonRequestedLanguageServicePendingChanges:
                        logger.Logf("Reason: RequestedLanguageService (pending file changes) - {0}", getDetails());
                        break;
                    case UpdateReasonRequestedLanguageServiceProjectNotLoaded:
                        logger.Logf("Reason: RequestedLanguageService (project not loaded) - {0}", getDetails());
                        break;
                    case UpdateReasonRequestedLanguageServiceForFileNotOpen:
                        logger.Logf("Reason: RequestedLanguageService (file not open) - {0}", getDetails());
                        break;
                    case UpdateReasonRequestedLanguageServiceProjectDirty:
                        logger.Logf("Reason: RequestedLanguageService (project dirty) - {0}", getDetails());
                        break;
                    case UpdateReasonRequestedLoadProjectTree:
                        logger.Logf("Reason: RequestedLoadProjectTree - {0}", getDetails());
                        break;
                    case UpdateReasonIdleCleanDiskCache:
                        logger.Logf("Reason: IdleCleanDiskCache");
                        break;
                    case UpdateReasonDidChangeConfigFile:
                        logger.Logf("Reason: DidChangeConfigFile - {0}", getDetails());
                        break;
                    case UpdateReasonDidChangeContentMapperContributions:
                        logger.Logf("Reason: DidChangeContentMapperContributions - {0}", getDetails());
                        break;
                }
            }

            var start = Stopwatch.GetTimestamp();
            var inferredContentMappers = _inferredProjectContentMappers;
            var inferredContentMapperExtensions = _inferredProjectContentMapperExtensions;
            if (change._contentMapperContributions is not null)
            {
                inferredContentMappers = change._contentMapperContributions.Mappers;
                inferredContentMapperExtensions = change._contentMapperContributions.Extensions;
            }

            var baseFS = store._fs;
            if (change._fs is not null)
            {
                baseFS = change._fs;
            }

            // Total replacements and returning to the session host must not retain files
            // from the previous filesystem. Layers invalidate only their per-path changes,
            // including the first layer over a host-backed snapshot.
            if (change._replaceFileSystem || _fileSystemOverride && !change._fileSystemOverride)
            {
                change._fileChanges.InvalidateAll = true;
            }

            var layeredFS = OverlayFS.LayerOverlayFileSystem(baseFS, overlays, store._options.PositionEncoding, store._toPath);
            overlays = layeredFS.Overlays();
            var fs = SnapshotFSBuilder.NewSnapshotFSBuilderFromSource(layeredFS, _fs._cacheFiles, _fs._cacheDirectories, _fs._nodeModulesRealpathAliases, store._toPath);
            change._fileChanges = ProcessFileChanges(fs, change._fileChanges, logger, change._contentMapperContributions, Overlays(), overlays);

            var compilerOptionsForInferredProjects = _compilerOptionsForInferredProjects;
            if (change._compilerOptionsForInferredProjects is not null)
            {
                compilerOptionsForInferredProjects = change._compilerOptionsForInferredProjects;
            }

            // Compute effective customConfigFileName from user preferences
            var customConfigFileName = ConfigFileRegistry._customConfigFileName;
            if (change._newConfig is not null)
            {
                customConfigFileName = change._newConfig.Value.CustomConfigFileName;
            }

            var newSnapshotID = store.NextSnapshotID();
            var projectCollectionBuilder = ProjectCollectionBuilder.NewProjectCollectionBuilder(
                ctx,
                newSnapshotID,
                fs,
                overlays,
                ProjectCollection,
                ConfigFileRegistry,
                ProjectCollection._apiState,
                compilerOptionsForInferredProjects,
                inferredContentMappers,
                inferredContentMapperExtensions,
                store._options,
                customConfigFileName,
                store._parseCache,
                store._contentMappedParseCache,
                store._extendedConfigCache,
                store._contentMapperHost,
                client);

            if (change._ataChanges is { Count: > 0 })
            {
                projectCollectionBuilder.DidUpdateATAState(change._ataChanges, logger?.Fork("DidUpdateATAState"));
            }

            projectCollectionBuilder.DidChangeCustomConfigFileName(logger?.Fork("DidChangeCustomConfigFileName"));
            if (change._compilerOptionsForInferredProjects is not null && projectCollectionBuilder._inferredProject.Value() is { } inferredProjectValue)
            {
                projectCollectionBuilder.UpdateInferredProject(
                    inferredProjectValue.CommandLine!.FileNames(),
                    change._compilerOptionsForInferredProjects,
                    inferredProjectValue.CommandLine.ProjectReferences(),
                    inferredProjectValue.CommandLine.Errors,
                    inferredProjectValue.CommandLine.ContentMappers() ?? [],
                    logger?.Fork("DidChangeCompilerOptionsForInferredProjects"));
            }

            if (change._contentMapperContributions is not null)
            {
                projectCollectionBuilder.DidChangeContentMapperContributions(logger?.Fork("DidChangeContentMapperContributions"));
            }

            if (change._newConfig is not null)
            {
                projectCollectionBuilder.DidChangeUserPreferences(_userPreferences, change._newConfig.Value, logger?.Fork("DidChangeUserPreferences"));
            }

            if (!change._fileChanges.IsEmpty())
            {
                projectCollectionBuilder.DidChangeFiles(change._fileChanges, logger?.Fork("DidChangeFiles"));
            }

            Exception? apiError = null;
            if (change._apiRequest is not null)
            {
                apiError = projectCollectionBuilder.HandleAPIRequest(change._apiRequest, logger?.Fork("HandleAPIRequest"));
            }

            foreach (var uri in change.Documents ?? [])
            {
                projectCollectionBuilder.DidRequestFile(uri, false /*configuredProjectsOnly*/, logger?.Fork("DidRequestFile"));
            }

            foreach (var uri in change.ConfiguredProjectDocuments ?? [])
            {
                projectCollectionBuilder.DidRequestFile(uri, true /*configuredProjectsOnly*/, logger?.Fork("DidRequestFile (optional)"));
            }

            foreach (var projectId in change.Projects ?? [])
            {
                projectCollectionBuilder.DidRequestProject(projectId, logger?.Fork("DidRequestProject"));
            }

            if (change.ProjectTree is not null)
            {
                projectCollectionBuilder.DidRequestProjectTrees(change.ProjectTree, logger?.Fork("DidRequestProjectTrees"));
            }

            var (projectCollection, configFileRegistry) = projectCollectionBuilder.Finalize(logger);

            var projectsWithNewProgramStructure = new Dictionary<ProjectID, bool>();
            foreach (var project in projectCollection.Projects())
            {
                if (project.ProgramLastUpdate == newSnapshotID && project.ProgramUpdateKind != ProgramUpdateKindCloned)
                {
                    projectsWithNewProgramStructure[project.ID()] = project.ProgramUpdateKind == ProgramUpdateKindNewFiles;
                }
            }

            // Clean cached files not touched by any open project on file open, close, delete,
            // or when explicitly requested (e.g. by an idle timer).
            var shouldCleanFileCache = change._cleanFileCache ||
                change._fileChanges.Opened != default ||
                change._fileChanges.Reopened != default ||
                change._fileChanges.Closed.Len() > 0 ||
                change._fileChanges.Deleted.Len() > 0;
            if (shouldCleanFileCache)
            {
                // The set of seen files can change only if a program was constructed (not cloned) during this snapshot.
                // When cleanFileCache is explicitly set, always attempt cleaning.
                if (projectsWithNewProgramStructure.Count > 0 || change._cleanFileCache)
                {
                    var cleanFilesStart = Stopwatch.GetTimestamp();
                    var removedFiles = 0;
                    fs._cacheFiles.Range(entry =>
                    {
                        foreach (var project in projectCollection.Projects())
                        {
                            if (project._host is not null && project._host._sourceFS.SeenFile(entry._key))
                            {
                                return true;
                            }
                        }

                        entry.Delete();
                        removedFiles++;
                        return true;
                    });
                    logger?.Logf("Removed {0} cached file(s) in {1}", removedFiles, Stopwatch.GetElapsedTime(cleanFilesStart));
                }
            }

            var config = _userPreferences;
            if (change._newConfig is not null)
            {
                config = change._newConfig.Value;
            }

            var autoImportHost = AutoImport.NewAutoImportRegistryCloneHost(
                projectCollection,
                store._parseCache,
                fs,
                store._options.CurrentDirectory,
                store._toPath);
            var openFiles = new Dictionary<Path, string>(overlays.Count);
            foreach (var (path, overlay) in overlays)
            {
                openFiles[path] = overlay.FileName();
            }

            var prepareAutoImports = new Path("");
            if (change.AutoImports != default)
            {
                prepareAutoImports = change.AutoImports.Path(UseCaseSensitiveFileNames());
            }

            var oldAutoImports = AutoImports;
            oldAutoImports ??= LsAutoImport.AutoImport.NewRegistry(store._toPath, _userPreferences);
            WatchedFiles<Dictionary<Path, string>>? autoImportsWatch = null;
            var autoImports = oldAutoImports.Clone(ctx, new RegistryChange
            {
                RequestedFile = prepareAutoImports,
                OpenFiles = openFiles,
                Changed = change._fileChanges.Changed,
                Created = change._fileChanges.Created,
                Deleted = change._fileChanges.Deleted,
                RebuiltPrograms = projectsWithNewProgramStructure,
                UserPreferences = change._newConfig,
            }, autoImportHost, logger?.Fork("UpdateAutoImports"));
            autoImportsWatch = _autoImportsWatch!.Clone(autoImports.NodeModulesDirectories());

            var snapshotFS = fs.Finalize().Snapshot;
            var newSnapshot = NewSnapshot(
                store,
                newSnapshotID,
                snapshotFS,
                null,
                compilerOptionsForInferredProjects,
                config,
                autoImports,
                autoImportsWatch);
            newSnapshot._parentId = _id;
            newSnapshot.ProjectCollection = projectCollection;
            newSnapshot.ConfigFileRegistry = configFileRegistry;
            newSnapshot._inferredProjectContentMappers = inferredContentMappers;
            newSnapshot._inferredProjectContentMapperExtensions = inferredContentMapperExtensions;
            newSnapshot._builderLogs = logger;
            newSnapshot._apiError = apiError;
            newSnapshot._fileSystemOverride = change._fileSystemOverride;
            newSnapshot._createdPrograms = projectCollectionBuilder._createdPrograms;

            foreach (var project in newSnapshot.ProjectCollection.Projects())
            {
                if (project.Program is not null)
                {
                    store._programCounter.Ref(project.Program);
                    if (project.ProgramLastUpdate == newSnapshotID)
                    {
                        // If the program was updated during this clone, the project and its host are new
                        // and still retain references to the builder. Freezing clears the builder reference
                        // so it's GC'd and to ensure the project can't access any data not already in the
                        // snapshot during use. This is pretty kludgy, but it's an artifact of Program design:
                        // Program has a single host, which is expected to implement a full vfs.FS, among
                        // other things. That host is *mostly* only used during program *construction*, but a
                        // few methods may get exercised during program *use*. So, our compiler host is allowed
                        // to access caches and perform mutating effects (like acquire referenced project
                        // config files) during snapshot building, and then we call `freeze` to ensure those
                        // mutations don't happen afterwards. In the future, we might improve things by
                        // separating what it takes to build a program from what it takes to use a program,
                        // and only pass the former into NewProgram instead of retaining it indefinitely.
                        project._host!.Freeze(snapshotFS, newSnapshot.ConfigFileRegistry);
                    }
                }
            }

            foreach (var configEntry in newSnapshot.ConfigFileRegistry._configs.Values)
            {
                if (configEntry._commandLine?.ConfigFile is not null)
                {
                    foreach (var file in configEntry._commandLine.ConfigFile.ExtendedSourceFiles ?? [])
                    {
                        store._extendedConfigCache.AddOwner(store._toPath(file), newSnapshot._id);
                    }
                }
            }

            autoImportHost.Dispose();

            logger?.Logf("Finished cloning snapshot {0} into snapshot {1} in {2}", _id, newSnapshot._id, Stopwatch.GetElapsedTime(start));
            return newSnapshot;
        }
        catch when (loggingEnabled)
        {
            // Go: defer func() { if r := recover(); r != nil { sessionLogger.Log(logger.String()); panic(r) } }()
            sessionLogger!.Log(logger?.String() ?? "");
            throw;
        }
    }

    /// <summary>ref increments the snapshot's reference count, preventing it from being
    /// disposed until a corresponding Deref is called. The snapshot must still
    /// be alive (refCount > 0) when ref is called（snapshot.go:733-738）。</summary>
    internal void Ref()
    {
        var n = Interlocked.Increment(ref _refCount);
        if (n <= 1)
        {
            // Go: panic(fmt.Sprintf("snapshot %d: ref on disposed snapshot, parentId=%d", s.id, s.parentId))
            Debug.Fail($"snapshot {_id}: ref on disposed snapshot, parentId={_parentId}");
        }
    }

    /// <summary>tryRef attempts to increment the snapshot's reference count. If the
    /// snapshot is already disposed (refCount == 0), it returns false without
    /// modifying the count. On success the caller must eventually call Deref（snapshot.go:742-754）。</summary>
    internal bool TryRef()
    {
        while (true)
        {
            var rc = Volatile.Read(ref _refCount);
            if (rc <= 0)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _refCount, rc + 1, rc) == rc)
            {
                return true;
            }
        }
    }

    /// <summary>Deref decrements the snapshot's reference count. When the count reaches
    /// zero, the snapshot is disposed and its store-owned resources are released（snapshot.go:758-766）。</summary>
    public void Deref()
    {
        var rc = Interlocked.Decrement(ref _refCount);
        if (rc < 0)
        {
            // Go: panic(fmt.Sprintf("snapshot %d: ref count below zero, parentId=%d", s.id, s.parentId))
            Debug.Fail($"snapshot {_id}: ref count below zero, parentId={_parentId}");
        }

        if (rc == 0)
        {
            Dispose();
        }
    }

    /// <summary>Go: <c>func (s *Snapshot) dispose()</c>（snapshot.go:768-806）。</summary>
    internal void Dispose()
    {
        var store = _host;
        foreach (var project in ProjectCollection.Projects())
        {
            if (project.Program is not null && store._programCounter.Deref(project.Program))
            {
                var contentMapperProject = project.Program.ContentMapperProject();
                if (contentMapperProject is not null)
                {
                    contentMapperProject.Close();
                }

                // This program is no longer referenced by any snapshot.
                // Mark its checker pool as discarded so its idle-cleanup timer stops
                // keeping the pool alive, allowing the pool and any idle checkers it
                // still references to be reclaimed when the pool is garbage-collected.
                project._checkerPool?.Discard();
                foreach (var file in project.Program.SourceFiles())
                {
                    if (!file.IsContentMapperFailureStub() && !file.IsContentMapperSupplemental())
                    {
                        if (file.ContentMapper() != "")
                        {
                            store._contentMappedParseCache.Deref(ContentMappedParseCacheKey.ContentMappedParseCacheKeyForFile(file));
                        }
                        else
                        {
                            store._parseCache.Deref(ParseCacheKey.ParseCacheKeyForFile(file));
                        }
                    }
                }

                foreach (var file in project.Program.DuplicateSourceFiles())
                {
                    if (!file.IsContentMapperFailureStub)
                    {
                        if (file.ContentMapper != "")
                        {
                            store._contentMappedParseCache.Deref(ContentMappedParseCacheKey.ContentMappedParseCacheKeyForDuplicate(file));
                        }
                        else
                        {
                            store._parseCache.Deref(ParseCacheKey.ParseCacheKeyForDuplicate(file));
                        }
                    }
                }
            }
        }

        foreach (var configEntry in ConfigFileRegistry._configs.Values)
        {
            if (configEntry._commandLine is not null)
            {
                foreach (var file in configEntry._commandLine.ExtendedSourceFiles() ?? [])
                {
                    store._extendedConfigCache.Release(store._toPath(file), _id);
                }
            }
        }
    }
}

/// <summary>Go 的 <c>APICreateProgramRequest</c>（snapshot.go:323-331）。
/// 非 sealed：`APIReconfigureProgramRequest` 内嵌它（见下）。</summary>
public class APICreateProgramRequest
{
    public string[] RootFileNames = [];

    public CompilerOptions? CompilerOptions;

    public ProjectReference[]? ProjectReferences;

    public Ast.Diagnostic[]? ConfigFileParsingDiagnostics;

    public ModuleResolverFactory? ModuleResolverFactory;

    public ulong ModuleResolverID;
}

/// <summary>Go 的 <c>ModuleResolverFactory interface</c>（snapshot.go:333-335）。</summary>
public interface ModuleResolverFactory
{
    /// <summary>Go: <c>NewResolver(options module.ResolverOptions) (module.Resolver, func())</c>。</summary>
    (Module.Resolver Resolver, Action Release) NewResolver(Module.ResolverOptions options);
}

/// <summary>Go 的 <c>APIReconfigureProgramRequest</c>（snapshot.go:337-340，内嵌 ⇒ C# 继承）。</summary>
public sealed class APIReconfigureProgramRequest : APICreateProgramRequest
{
    public SyntheticProjectID ProgramID;
}

/// <summary>Go 的 <c>APISnapshotRequest</c>（snapshot.go:342-357）。</summary>
public sealed class APISnapshotRequest
{
    public Set<string>? OpenProjects;

    public Set<Path>? CloseProjects;

    public Dictionary<Path, string>? OpenFiles;

    public Set<Path>? CloseFiles;

    public APICreateProgramRequest[]? CreatePrograms;

    public APIReconfigureProgramRequest[]? ReconfigurePrograms;

    public Set<SyntheticProjectID>? RemovePrograms;

    public Set<ID>? EnsurePrograms;

    public bool EnsureAllPrograms;

    public Dictionary<Path, string>? EnsureFiles;

    public Vfs.FS? FileSystem;

    /// <summary>ReplaceFileSystem indicates a total filesystem replacement. Layers use
    /// per-path file changes instead of invalidating all inherited state.</summary>
    public bool ReplaceFileSystem;
}

/// <summary>Go 的 <c>ProjectTreeRequest</c>（snapshot.go:359-361）。</summary>
public sealed class ProjectTreeRequest
{
    /// <summary>If null, all project trees need to be loaded, otherwise only those that are referenced.</summary>
    internal Set<Path>? _referencedProjects;

    public ProjectTreeRequest()
    {
    }

    public ProjectTreeRequest(Set<Path>? referencedProjects)
    {
        _referencedProjects = referencedProjects;
    }

    public bool IsAllProjects()
    {
        return _referencedProjects is null;
    }

    public bool IsProjectReferenced(Path projectID)
    {
        return _referencedProjects!.Has(projectID);
    }

    public Path[]? Projects()
    {
        if (_referencedProjects is null)
        {
            return null;
        }

        return [.. _referencedProjects.Keys()];
    }
}

/// <summary>Go 的 <c>ResourceRequest</c>（snapshot.go:377-393）。</summary>
public class ResourceRequest
{
    /// <summary>Documents are URIs that were requested by the client.
    /// The new snapshot should ensure projects for these URIs have loaded programs.</summary>
    public LsProto.DocumentUri[]? Documents;

    /// <summary>ConfiguredProjectDocuments are URIs for which configured projects should be loaded
    /// (if disableSolutionSearching/disableReferencedProjectLoad settings allow),
    /// but no inferred project should be created if no configured project is found.
    /// This is used by cross-project operations like find-all-references.</summary>
    public LsProto.DocumentUri[]? ConfiguredProjectDocuments;

    /// <summary>Update requested Projects.
    /// this is used when we want to get LS and from all the Projects the file can be part of.</summary>
    public ID[]? Projects;

    /// <summary>Update and ensure project trees that reference the projects
    /// This is used to compute the solution and project tree so that
    /// we can find references across all the projects in the solution irrespective of which project is open.</summary>
    public ProjectTreeRequest? ProjectTree;

    /// <summary>AutoImports is the document URI for which auto imports should be prepared.</summary>
    public LsProto.DocumentUri AutoImports;

    /// <summary>Go 的 `SnapshotChange{ResourceRequest: x}`（整块结构体复制）→ 浅复制整个请求。</summary>
    public void CopyFrom(ResourceRequest other)
    {
        Documents = other.Documents;
        ConfiguredProjectDocuments = other.ConfiguredProjectDocuments;
        Projects = other.Projects;
        ProjectTree = other.ProjectTree;
        AutoImports = other.AutoImports;
    }
}

/// <summary>Go 的 <c>SnapshotChange</c>（snapshot.go:395-412，内嵌 ResourceRequest ⇒ 继承，见文件头第 6 条）。</summary>
public sealed class SnapshotChange : ResourceRequest
{
    internal UpdateReason _reason;

    /// <summary>fs overrides the session filesystem for this snapshot. It is used by API
    /// snapshots that supply their own memory or cache filesystem.</summary>
    internal Vfs.FS? _fs;

    internal bool _fileSystemOverride;

    internal bool _replaceFileSystem;

    /// <summary>fileChanges are the changes that have occurred since the last snapshot.</summary>
    internal FileChangeSummary _fileChanges = new();

    /// <summary>compilerOptionsForInferredProjects is the compiler options to use for inferred projects.
    /// It should only be set the value in the next snapshot should be changed. If nil, the
    /// value from the previous snapshot will be copied to the new snapshot.</summary>
    internal CompilerOptions? _compilerOptionsForInferredProjects;

    internal ContentMapperContributions? _contentMapperContributions;

    internal UserPreferences? _newConfig;

    /// <summary>ataChanges contains ATA-related changes to apply to projects in the new snapshot.</summary>
    internal Dictionary<ID, ATAStateChange>? _ataChanges;

    internal APISnapshotRequest? _apiRequest;

    /// <summary>cleanFileCache triggers cleaning of cached files not referenced by any open project.</summary>
    internal bool _cleanFileCache;
}

/// <summary>ATAStateChange represents a change to a project's ATA state（snapshot.go:415-423）。</summary>
public sealed class ATAStateChange
{
    /// <summary>TypingsInfo is the new typings info for the project.</summary>
    public Ata.TypingsInfo? TypingsInfo;

    /// <summary>TypingsFiles is the new list of typing files for the project.</summary>
    public string[] TypingsFiles = [];

    /// <summary>TypingsFilesToWatch is the new list of typing files to watch for changes.</summary>
    public string[] TypingsFilesToWatch = [];

    public LogTree? Logs;
}
