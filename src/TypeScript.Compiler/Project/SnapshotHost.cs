// ported from: internal/project/snapshothost.go:1-188
//
// 与 Go 的偏离（逐条说明）：
//
// 1) **包级函数的落点**：Go 的 `project` 包与类型 `Project` 同名，C# 里不能再声明 `static class Project`，
//    所以包级函数挂到"它构造/返回的类型"上（仓内先例 `Locale.Parse`、`Project.NewConfiguredProject`）：
//      `NewSnapshotHost`（已是导出）→ `SnapshotHost.NewSnapshotHost`；
//      `newContentMapperHost`（Go 未导出）→ `SnapshotHost.NewContentMapperHost`（private static；只有构造函数用）。
//
// 2) `snapshotID atomic.Uint64` → `private ulong _snapshotID` + `Interlocked.Increment`
//    （`atomic.Uint64.Add(1)` 的返回值语义：Go 返回**加之后**的值，`Interlocked.Increment` 同）。
//    保持 `ulong`（Go 的 uint64）而不是 long：快照 ID 只用于相等比较与日志。
//
// 3) `SourceFileLease.releaseOnce sync.Once` → **标志 + `System.Threading.Lock` 双检**
//    （本仓约定，见 Project.cs 文件头第 4 条）。Go 的 `sync.Once` 保证 `Release` 恰好执行一次；
//    这里 `lock` 块判断 + 置位，等价。
//
// 4) `CloneSnapshot(...) (*Snapshot, error)` 保留为元组 `(Snapshot Snapshot, Exception? Error)`：
//    Go 这里返回的 error 是 `snapshot.apiError`（一个**值**，调用方 `if err != nil` 检查后仍可能使用该
//    快照），不是"失败即展开"的控制流，所以不套用 §3.2 的"抛异常"约定（抛出去会丢掉快照本身）。
//    `Session.CloneSnapshot` 的调用点（集成切片/测试）按 `var (snap, err) = …` 解构。
//
// 5) `vfs.FS` → `Vfs.FS`；`lsutil.UserPreferences` → `UserPreferences`（`using TypeScript.LsUtil;`）；
//    `lsconv` 不涉及。
//
// 6) `lsutil.NewDefaultUserPreferences()` → `UserPreferences.NewDefaultUserPreferences()`（静态工厂在类型上）。
//
// 7) `NewWatchedFiles(...)` 挂在泛型类型上（ProjWatch 的落点）⇒ 必须写出类型实参：
//    `WatchedFiles<Dictionary<Path, string>>.NewWatchedFiles(...)`。
//    `getRecursiveGlobPattern` → `Watch.GetRecursiveGlobPattern`；`slices.Sort` → `Array.Sort(…, StringComparer.Ordinal)`。
//
// 8) 导出的 `NewRootSnapshot()` 与未导出的 `newRootSnapshot(id, relativePatternSupport)` 在 C# 里是同名重载
//    `NewRootSnapshot()` / `NewRootSnapshot(ulong id, bool relativePatternSupport)`。
//
// 9) `contentmapper.NewHostWithOptions` → `ContentMapper.NewHostWithOptions`（`using static` 的裸名）。
//    `contentmapper.Host` 是接口 → `ContentMapper.Host?`。
//
// 10) `NewCachedFileHandle` 在 C# 落在 `CachedFile` 上（OverlayFs.cs 的落点），见其文件头；本文件写
//     `CachedFile.NewCachedFileHandle(...)`。
//
// 11) **`Session` 与 `SnapshotHost` 的“同一对象”语义**：Go 的 `Session{SnapshotHost: snapshotHost}`（session.go:232）
//     是**内嵌同一个指针**，所以 `s.contentMapperHost`/`s.parseCache`/`s.programCounter` … 就是宿主的那几个字段；
//     C# 无法把已存在的基类实例挂到 `this` 上，故新增 `internal void AdoptHost(SnapshotHost other)`，由
//     `Session` 的构造函数把宿主的 8 个字段逐个搬到 `this`（并由 `this` 建根快照，使 `snapshot._host == session`）。
//     **漏搬字段就是真 bug**：`Session.Configure` 的 `_contentMapperHost?.SetLocale(...)`（session.go:302）、
//     `TakeContentMapperTimingDelta`、`GetStats` 的 `_parseCache`/`_extendedConfigCache`/`_programCounter`
//     以及继承来的 `Close()`（`s.SnapshotHost.Close()`，session.go:1695）都会读到 null 而静默空转。
using System.Threading;
using TypeScript.Ast;
using TypeScript.ContentMapper;
using TypeScript.Core;
using TypeScript.LsUtil;
using TypeScript.Vfs;
using Loc = TypeScript.Locale.Locale;
using Path = TypeScript.TSPath.Path;
using TSOpts = TypeScript.TsOptions.TsOptions;
using static TypeScript.ContentMapper.ContentMapper;
using static TypeScript.LsUtil.LsUtil;
using static TypeScript.Project.UpdateReason;
using static TypeScript.TSPath.TSPath;

namespace TypeScript.Project;

/// <summary>Go 的 <c>SnapshotHost</c>（snapshothost.go:17-36）：一组不可变快照共享的服务。
/// 类声明上的 <c>TSOpts.ParseConfigHost</c> 是**非 Go 源的必要写法**：Go 的 `*project.SnapshotHost` 只靠
/// `FS()` + `GetCurrentDirectory()` **结构性**满足 `tsoptions.ParseConfigHost`（`internal/api` 的
/// `handleParseCommandLine`/`handleParseJsonConfigFileContent` 直接把 snapshotHost 传进去），C# 的接口是名义的，
/// 必须显式列出（先例：Compiler/Host.cs 文件头第 6 条、Execute/Tsc/Compile.cs 的 `System : Spawner, ParseConfigHost`、
/// Project/ConfigFileRegistryBuilder.cs:71）。</summary>
public class SnapshotHost : TSOpts.ParseConfigHost
{
    internal SessionOptions _options = null!;

    internal Func<string, Path> _toPath = null!;

    internal Vfs.FS _fs = null!;

    internal ParseCache _parseCache = null!;

    internal ContentMappedParseCache _contentMappedParseCache = null!;

    internal ExtendedConfigCache _extendedConfigCache = null!;

    internal ProgramCounter _programCounter = null!;

    internal ContentMapper.Host? _contentMapperHost;

    /// <summary>Go: <c>snapshotID atomic.Uint64</c>（见文件头第 2 条）。</summary>
    private ulong _snapshotID;

    internal ulong NextSnapshotID()
    {
        return Interlocked.Increment(ref _snapshotID);
    }

    internal SourceFileLease AcquireSourceFile(SourceFileParseOptions options, string text, ScriptKind scriptKind)
    {
        var fileHandle = CachedFile.NewCachedFileHandle(options.FileName, text);
        var key = ParseCacheKey.NewParseCacheKey(options, fileHandle.Hash(), scriptKind);
        return new SourceFileLease
        {
            _cache = _parseCache,
            _key = key,
            _sourceFile = _parseCache.Acquire(key, fileHandle),
        };
    }

    /// <summary>
    /// Go: `Session{SnapshotHost: snapshotHost, …}`（session.go:232-244）—— Go 的内嵌让 Session 与 SnapshotHost
    /// 是**同一个对象**（`s.contentMapperHost` 就是 `snapshotHost.contentMapperHost`）。C# 无法把一个已存在的基类实例
    /// 挂到 <c>this</c> 上，故由 <see cref="Session"/> 的构造函数把 <see cref="NewSnapshotHost"/> 建好的宿主字段
    /// 逐个搬到 <c>this</c>。<b>漏搬字段就是真 bug</b>：Session 里裸读 `_contentMapperHost`（`Configure` 的
    /// `SetLocale`、`TakeContentMapperTimingDelta`）、`_parseCache`/`_extendedConfigCache`/`_programCounter`
    /// （`GetStats` 日志）以及继承来的 `Close()`（session.go:1683-1696 的 `s.SnapshotHost.Close()`）都会读到 null。
    /// </summary>
    internal void AdoptHost(SnapshotHost other)
    {
        _options = other._options;
        _toPath = other._toPath;
        _fs = other._fs;
        _parseCache = other._parseCache;
        _contentMappedParseCache = other._contentMappedParseCache;
        _extendedConfigCache = other._extendedConfigCache;
        _programCounter = other._programCounter;
        _contentMapperHost = other._contentMapperHost;
        _snapshotID = other._snapshotID;
    }

    /// <summary>Go: <c>func NewSnapshotHost(init *SessionInit) *SnapshotHost</c>（snapshothost.go:52-81）。</summary>
    public static SnapshotHost NewSnapshotHost(SessionInit init)
    {
        var currentDirectory = init.Options.CurrentDirectory;
        var useCaseSensitiveFileNames = init.FS.UseCaseSensitiveFileNames();
        var toPath = (string fileName) => ToPath(fileName, currentDirectory, useCaseSensitiveFileNames);
        var parseCache = init.ParseCache ?? ParseCache.NewParseCache(new RefCountCacheOptions());
        var contentMappedParseCache = init.ContentMappedParseCache ?? ContentMappedParseCache.NewContentMappedParseCache(new RefCountCacheOptions());

        return new SnapshotHost
        {
            _options = init.Options,
            _toPath = toPath,
            _fs = init.FS,
            _parseCache = parseCache,
            _contentMappedParseCache = contentMappedParseCache,
            _extendedConfigCache = ExtendedConfigCache.NewExtendedConfigCache(),
            _programCounter = new ProgramCounter(),
            _contentMapperHost = NewContentMapperHost(init),
        };
    }

    /// <summary>Go: <c>func newContentMapperHost(init *SessionInit) contentmapper.Host</c>（session.go:194-202，见文件头第 1 条）。
    /// 工作区受信任且能起进程时才建 host；否则返回 null（配置文件的 gate 会拒绝 content mapper）。</summary>
    private static ContentMapper.Host? NewContentMapperHost(SessionInit init)
    {
        if (!init.Options.RunExternalCode || init.Spawner is null)
        {
            return null;
        }

        var diagnosticLocale = Loc.Default;
        if (init.Client is not null)
        {
            diagnosticLocale = init.Client.GetLocale();
        }

        return NewHostWithOptions(init.BackgroundCtx, init.Spawner, diagnosticLocale, new HostOptions { Logger = init.ContentMapperLogger });
    }

    /// <summary>Go: <c>func (s *SnapshotHost) NewRootSnapshot() *Snapshot</c>：创建一个独立的根快照。</summary>
    public Snapshot NewRootSnapshot()
    {
        return NewRootSnapshot(0, false);
    }

    /// <summary>Go: <c>func (s *SnapshotHost) RetainSnapshot(snapshot *Snapshot)</c>：给本 host 拥有的快照加一个引用。</summary>
    public void RetainSnapshot(Snapshot snapshot)
    {
        snapshot.Ref();
    }

    /// <summary>Go: <c>func (s *SnapshotHost) CloneSnapshot(ctx, baseSnapshot, fileChanges, apiRequest) (*Snapshot, error)</c>
    /// （snapshothost.go:90-107，返回形状见文件头第 4 条）。</summary>
    public (Snapshot Snapshot, Exception? Error) CloneSnapshot(
        Context ctx,
        Snapshot baseSnapshot,
        FileChangeSummary fileChanges,
        APISnapshotRequest? apiRequest)
    {
        var change = new SnapshotChange
        {
            _apiRequest = apiRequest,
            _fileChanges = fileChanges,
        };
        if (apiRequest is not null)
        {
            change._fs = apiRequest.FileSystem;
            change._fileSystemOverride = apiRequest.FileSystem is not null;
            change._replaceFileSystem = apiRequest.ReplaceFileSystem;
        }

        var snapshot = Update(ctx, baseSnapshot, change);
        return (snapshot, snapshot._apiError);
    }

    /// <summary>Go: <c>func (s *SnapshotHost) update(ctx, baseSnapshot, change) *Snapshot</c>（snapshothost.go:111-113）。</summary>
    internal Snapshot Update(Context ctx, Snapshot baseSnapshot, SnapshotChange change)
    {
        return baseSnapshot.Clone(ctx, change, baseSnapshot.Overlays(), null, null);
    }

    /// <summary>Go: <c>func (s *SnapshotHost) CloneSnapshotWithAutoImports(ctx, baseSnapshot, uri, logger) *Snapshot</c>
    /// （snapshothost.go:116-125）。</summary>
    public Snapshot CloneSnapshotWithAutoImports(Context ctx, Snapshot baseSnapshot, LsProto.DocumentUri uri, Logging.Logger? logger)
    {
        var change = new SnapshotChange
        {
            _reason = UpdateReasonRequestedLanguageServiceWithAutoImports,
            _fs = baseSnapshot._fs._fs,
            _fileSystemOverride = baseSnapshot._fileSystemOverride,
        };
        change.CopyFrom(baseSnapshot.ResourceRequestForDocument(uri));
        change.AutoImports = uri;
        return baseSnapshot.Clone(ctx, change, baseSnapshot.Overlays(), logger, null);
    }

    /// <summary>Go: <c>func (s *SnapshotHost) newRootSnapshot(id uint64, relativePatternSupport bool) *Snapshot</c>（snapshothost.go:127-157）。</summary>
    internal Snapshot NewRootSnapshot(ulong id, bool relativePatternSupport)
    {
        var fileSystem = OverlayFS.NewOverlayFS(_fs, [], _options.PositionEncoding, _toPath);
        return Snapshot.NewSnapshot(
            this,
            id,
            new SnapshotFS
            {
                _toPath = _toPath,
                _fs = fileSystem,
            },
            new ConfigFileRegistry(),
            null,
            NewDefaultUserPreferences(),
            null,
            WatchedFiles<Dictionary<Path, string>>.NewWatchedFiles(
                "auto-import",
                LsProto.WatchKind.Create | LsProto.WatchKind.Change | LsProto.WatchKind.Delete,
                relativePatternSupport,
                nodeModulesDirs =>
                {
                    // Go 的 `for _, dir := range nodeModulesDirs` 对 **nil map** 是空迭代、`len(nil)` 是 0
                    // ⇒ C# 必须容忍 null（`NodeModulesDirectories()` 在没有 node_modules 时返回 null）。
                    var patterns = new List<string>(nodeModulesDirs?.Count ?? 0);
                    foreach (var dir in nodeModulesDirs?.Values ?? (IEnumerable<string>)[])
                    {
                        patterns.Add(Watch.GetRecursiveGlobPattern(dir));
                    }

                    patterns.Sort(StringComparer.Ordinal);
                    return new PatternsAndIgnored
                    {
                        _patternsInsideWorkspace = [.. patterns],
                    };
                }));
    }

    public Vfs.FS FS()
    {
        return _fs;
    }

    public string GetCurrentDirectory()
    {
        return _options.CurrentDirectory;
    }

    public string DefaultLibraryPath()
    {
        return _options.DefaultLibraryPath;
    }

    public void Close()
    {
        _contentMapperHost?.Close();
    }
}

/// <summary>Go 的 <c>SourceFileLease</c>（snapshothost.go:38-50）：一个被引用计数的已解析源文件租约。</summary>
public sealed class SourceFileLease
{
    internal ParseCache _cache = null!;
    internal ParseCacheKey _key = null!;
    internal SourceFile _sourceFile = null!;

    /// <summary>Go: <c>releaseOnce sync.Once</c>（见文件头第 3 条）。</summary>
    private readonly Lock _releaseLock = new();
    private bool _released;

    public SourceFile SourceFile()
    {
        return _sourceFile;
    }

    /// <summary>Go: <c>func (l *SourceFileLease) Release()</c>。</summary>
    public void Release()
    {
        lock (_releaseLock)
        {
            if (_released)
            {
                return;
            }

            _released = true;
        }

        _cache.Deref(_key);
    }
}
