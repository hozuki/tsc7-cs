// ported from: internal/project/projectcollectionbuilder.go:1-1637
//
// 与 Go 的偏离（逐条说明）：
//
// 1) **包级函数落点**：Go 的 `project` 包名与类型 `Project` 同名 ⇒ 不能建 `static class Project`
//    （先例 `Locale.Parse`，见 Project.cs 文件头第 1 条）。本文件的包级函数挂到最相关类型上：
//      - `newProjectCollectionBuilder` → `ProjectCollectionBuilder.NewProjectCollectionBuilder(...)`；
//      - `logChangeFileResult` → `ProjectCollectionBuilder.LogChangeFileResult(...)`（静态）；
//      - `projectReferencesEqual` → `ProjectCollectionBuilder.ProjectReferencesEqual(...)`（静态）；
//      - `openFilePaths`/`findDefaultConfiguredProjectFromProgramInclusion` 已由 M8Project 落到
//        `ProjectCollection.OpenFilePaths`/`ProjectCollection.FindDefaultConfiguredProjectFromProgramInclusion`（直接调用）。
//
// 2) **`projectLoadKind`（未导出）→ `ProjectLoadKind`**（本文件定义），成员保留 Go 常量名
//    `ProjectLoadKindFind`/`ProjectLoadKindCreate`（与 `Kind`/`ProgramUpdateKind` 同一套做法），
//    文件顶 `using static TypeScript.Project.ProjectLoadKind;` 后**裸写**（与 Go 逐字一致）。
//
// 3) **未导出类型命名**：`searchNode` → `internal sealed class SearchNode`、`searchNodeKey` →
//    `internal readonly record struct SearchNodeKey(string ConfigFileName, ProjectLoadKind LoadKind)`
//    （BFS 的 visited 键需要值相等）、`searchResult` → `internal sealed class SearchResult`
//    （Go 按值返回/取地址，C# 用引用类型即可：调用点只读字段并 `retain.Add`）。
//
// 4) **`sync.WaitGroup` + `wg.Go`** → 本仓 `Core.WorkGroup`（`NewWorkGroup(false)` + `Queue`/`RunAndWait`，
//    见 Core/Workgroup.cs 文件头第 1 条）。`sync.WaitGroup` 的并行语义（全部跑完才继续）由 `RunAndWait` 承担，
//    异常在层栅栏处抛出（Go 的 goroutine panic 直接崩进程）。
//
// 5) **`time.Now()`/`time.Since`** → `Stopwatch.GetTimestamp()`/`Stopwatch.GetElapsedTime(start)`（本仓约定，
//    见 Project/Snapshot.cs、Ipc/ConnSync.cs）；日志格式串按本仓约定改写成 C# 复合格式 `{0}`（Logger.cs 文件头第 4 条）。
//
// 6) **`*dirty.Box[*Project]` → `Dirty.Box<Project>`**（不是 `Box<Project?>`：`Box<T> where T : Cloneable<T>`
//    对 `T = Project?` 是 CS8631，实测见 artifacts/nulprobe）。`Box.Delete()` 后 `Value()` 运行时为 null，
//    判空一律写 `x.Value() is not null`（Go 的 `b.inferredProject.Value() != nil`）。
//
// 7) **nil 切片**：Go 的 `var s []T` 是 nil 切片，C# 里要么用可空数组（`T[]?`）要么用 `!` 保留 nil
//    （`Project.NewSyntheticProject` 的 `compilerOptions`/`projectReferences` 是非空形参但 Go 会传 nil ⇒ 用 `!`，
//    运行时仍是 nil，语义与 Go 一致）。`!` 的调用点都加了注释。
//
// 8) **`reflect.DeepEqual`**（§3.6 禁反射）：
//      - `CompilerOptions` → 复用既有字段表 `TsOptions.TsOptions.CompilerOptionFields`（`Changed` 即逐字段 DeepEqual，
//        见 CompilerOptionFields.cs 文件头与 Execute/Watcher.cs 的先例）→ 本文件私有静态 `CompilerOptionsDeepEqual`；
//      - `[]*ast.Diagnostic` → `Ast.Ast.EqualDiagnostics` 逐元素比较（Go 的 DeepEqual 递归比较目标值；
//        仓内诊断的"同一性"就是 EqualDiagnostics 的那组字段）→ 私有静态 `DiagnosticsDeepEqual`；
//      - `[]*core.ProjectReference` → Go 自己的 `projectReferencesEqual`（只比 `Path`/`Circular`）照抄。
//
// 9) **`slices.Equal`（顺序敏感）** → `SequenceEqual`（`Enumerable.SequenceEqual`，字符串按 ordinal 比较，
//    与 Go 的 `==` 一致）；`slices.ContainsFunc(xs, f)` → `Array.Exists`；`slices.Concat(a,b,c)` → 展开表达式
//    `[.. a, .. b, .. c]`；`slices.Clone(x)` → `[.. x]`；`slices.Sort` → `Array.Sort(..., StringComparer.Ordinal)`
//    （Go 的字符串序 = 逐字节序）；`slices.Compact(slices.Sort(x))` → `Watch.SortedAndCompacted(x)`（E 组已落地）。
//
// 10) **`slices.Delete(xs, slices.Index(xs, f), slices.Index(xs, f)+1)`** → 私有静态 `DeleteFirst(xs, f)`
//     （删除**第一处**出现的元素，返回新数组）。Go 的 `slices.Delete` 可能原地改写目标数组；这里返回新数组，
//     因此不会改动 `CommandLine.FileNames()` 背后的数组（更安全，且对后续比较可观察等价）。
//
// 11) `panic(...)` → `Debug.Fail(...)`（`TypeScript.Debug`；**不** `using System.Diagnostics;`，
//     Stopwatch 用别名 `using Stopwatch = System.Diagnostics.Stopwatch;`）。
//
// 12) `logger.Fork`/`logger.Log`/`logger.Logf` 在 Go 里对 nil 接收者安全 ⇒ C# 写 `logger?.Fork(...)`/`logger?.Log(...)`
//     （同 Logging/LogTree.cs 文件头第 1 条）。`logger.Embed(logs)` 的 `Logs` 字段可空 ⇒ 先判空。
//
// 13) `map[K]struct{}`/`collections.Set[T]` → `Collections.Set<T>`（见 ConfigFileRegistry.cs 文件头第 3 条）；
//     Go 的 `Set` 方法对 nil 接收者/ nil map 安全，C# 的 `Set<T>` 是 class ⇒ 需要处写 `set?.Method()` 或显式判空
//     （本文件里 `retain`、`projectsToClose` 两处）。
//
// 14) `maps.Equal(a, b)`（`Dictionary<Path, ID>` 的相等）→ 本文件私有静态 `MapsEqual`。
//
// 15) `configFileEntry`/`changeFileResult` 的字段名带下划线（见 ConfigFileRegistry.cs / ConfigFileRegistryBuilder.cs）。
//
// 16) `_apiState._openProjects`/`_openFiles` 在 C# 里始终非 null（`APIState` 字段初始化即 `[]`），因此
//     Go 的 `if b.apiState.openProjects == nil { … = make(…) }` 分支不需要（语义等价：nil map 读 == 空 map 读）。
using System;
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.LsProto;
using TypeScript.Project.Logging;
using TypeScript.TsOptions;
using TypeScript.TSPath;
using TypeScript.Vfs;
using Ata = TypeScript.Project.Ata;
using AstDiag = TypeScript.Ast.Diagnostic;
using Dirty = TypeScript.Project.Dirty;
using Path = TypeScript.TSPath.Path;
using Stopwatch = System.Diagnostics.Stopwatch;
using static TypeScript.Collections.Collections;
using static TypeScript.Core.Core;
using static TypeScript.Project.Kind;
using static TypeScript.Project.ProgramUpdateKind;
using static TypeScript.Project.ProjectLoadKind;
using static TypeScript.TSPath.TSPath;

namespace TypeScript.Project;

/// <summary>Go 的未导出 <c>type projectLoadKind int</c>（projectcollectionbuilder.go:26-33）。</summary>
internal enum ProjectLoadKind
{
    /// <summary>Project is not created or updated, only looked up in cache。</summary>
    ProjectLoadKindFind = 0,

    /// <summary>Project is created and then its graph is updated。</summary>
    ProjectLoadKindCreate = 1,
}

/// <summary>Go 的 <c>ProjectCollectionBuilder</c>（projectcollectionbuilder.go:35-66）。</summary>
public sealed class ProjectCollectionBuilder
{
    internal SessionOptions _sessionOptions;
    internal ParseCache _parseCache;
    internal ContentMappedParseCache _contentMappedParseCache;
    internal ExtendedConfigCache? _extendedConfigCache;
    internal ContentMapper.Host? _contentMapperHost;
    internal Func<string, Path> _toPath;

    internal Context _ctx;
    internal SnapshotFSBuilder _fs;
    internal Dictionary<Path, Overlay> _overlays;
    internal ProjectCollection _base;
    internal CompilerOptions? _compilerOptionsForInferredProjects;
    internal ContentMapper.Mapper[] _inferredContentMappers;
    internal string[] _inferredContentMapperExtensions;
    internal ConfigFileRegistryBuilder _configFileRegistryBuilder;

    /// <summary>client is optional; used for project loading notifications。</summary>
    internal Client? _client;

    internal ulong _newSnapshotID;
    internal bool _programStructureChanged;
    internal bool _defaultProjectsInvalidated;
    internal bool _openFilesChanged;

    internal Dictionary<Path, ID> _fileDefaultProjects = [];
    internal Dirty.SyncMap<ConfiguredProjectID, Project> _configuredProjects;
    internal Dirty.SyncMap<SyntheticProjectID, Project> _syntheticProjects;
    internal Dirty.Box<Project> _inferredProject;
    internal Project[] _createdPrograms = [];

    internal APIState _apiState;

    /// <summary>
    /// Go: <c>func newProjectCollectionBuilder(...)</c>（projectcollectionbuilder.go:68-111；见文件头第 1 条）。
    /// </summary>
    internal static ProjectCollectionBuilder NewProjectCollectionBuilder(
        Context ctx,
        ulong newSnapshotID,
        SnapshotFSBuilder fs,
        Dictionary<Path, Overlay> overlays,
        ProjectCollection oldProjectCollection,
        ConfigFileRegistry oldConfigFileRegistry,
        APIState oldAPIState,
        CompilerOptions? compilerOptionsForInferredProjects,
        ContentMapper.Mapper[] inferredContentMappers,
        string[] inferredContentMapperExtensions,
        SessionOptions sessionOptions,
        string customConfigFileName,
        ParseCache parseCache,
        ContentMappedParseCache contentMappedParseCache,
        ExtendedConfigCache? extendedConfigCache,
        ContentMapper.Host? contentMapperHost,
        Client? client)
    {
        return new ProjectCollectionBuilder(
            ctx,
            newSnapshotID,
            fs,
            overlays,
            oldProjectCollection,
            oldConfigFileRegistry,
            oldAPIState,
            compilerOptionsForInferredProjects,
            inferredContentMappers,
            inferredContentMapperExtensions,
            sessionOptions,
            customConfigFileName,
            parseCache,
            contentMappedParseCache,
            extendedConfigCache,
            contentMapperHost,
            client);
    }

    /// <summary>Go 的结构体字面量（见文件头第 1 条）。</summary>
    internal ProjectCollectionBuilder(
        Context ctx,
        ulong newSnapshotID,
        SnapshotFSBuilder fs,
        Dictionary<Path, Overlay> overlays,
        ProjectCollection oldProjectCollection,
        ConfigFileRegistry oldConfigFileRegistry,
        APIState oldAPIState,
        CompilerOptions? compilerOptionsForInferredProjects,
        ContentMapper.Mapper[] inferredContentMappers,
        string[] inferredContentMapperExtensions,
        SessionOptions sessionOptions,
        string customConfigFileName,
        ParseCache parseCache,
        ContentMappedParseCache contentMappedParseCache,
        ExtendedConfigCache? extendedConfigCache,
        ContentMapper.Host? contentMapperHost,
        Client? client)
    {
        var openFiles = ProjectCollection.OpenFilePaths(overlays);
        _ctx = ctx;
        _fs = fs;
        _overlays = overlays;
        _toPath = fs._toPath;
        _compilerOptionsForInferredProjects = compilerOptionsForInferredProjects;
        _inferredContentMappers = inferredContentMappers;
        _inferredContentMapperExtensions = inferredContentMapperExtensions;
        _sessionOptions = sessionOptions;
        _parseCache = parseCache;
        _contentMappedParseCache = contentMappedParseCache;
        _extendedConfigCache = extendedConfigCache;
        _contentMapperHost = contentMapperHost;
        _base = oldProjectCollection;
        _configFileRegistryBuilder = ConfigFileRegistryBuilder.NewConfigFileRegistryBuilder(
            LsProto.LsProto.GetClientCapabilities().Workspace.DidChangeWatchedFiles.RelativePatternSupport,
            fs,
            path => overlays.ContainsKey(path),
            oldConfigFileRegistry,
            extendedConfigCache,
            newSnapshotID,
            sessionOptions,
            customConfigFileName,
            logger: null);
        _newSnapshotID = newSnapshotID;
        _openFilesChanged = !openFiles.Equals(oldProjectCollection._openFiles);
        _configuredProjects = NewDirtySyncMap(oldProjectCollection._configuredProjects);
        _syntheticProjects = NewDirtySyncMap(oldProjectCollection._syntheticProjects);
        _inferredProject = Dirty.Dirty.NewBox(oldProjectCollection._inferredProject!);
        _apiState = oldAPIState.Clone();
        _client = client;
    }

    /// <summary>Project.cs:733/764 用的是 PascalCase 属性名（`_host!._builder.ParseCache`），故补只读属性。</summary>
    internal ParseCache ParseCache => _parseCache;

    /// <summary>Project.cs:733/764 用的是 PascalCase 属性名（`_host!._builder.ContentMappedParseCache`）。</summary>
    internal ContentMappedParseCache ContentMappedParseCache => _contentMappedParseCache;

    private static Dirty.SyncMap<K, V> NewDirtySyncMap<K, V>(Dictionary<K, V>? @base)
        where K : notnull
        where V : Dirty.Cloneable<V>
    {
        return Dirty.Dirty.NewSyncMap(@base);
    }

    private bool IsOpenFile(Path path)
    {
        return _overlays.ContainsKey(path);
    }

    internal (ProjectCollection Collection, ConfigFileRegistry Registry) Finalize(LogTree? logger)
    {
        var changed = false;
        var newProjectCollection = _base;
        void EnsureCloned()
        {
            if (!changed)
            {
                newProjectCollection = newProjectCollection.Clone();
                changed = true;
            }
        }

        var (configuredProjects, configuredProjectsChanged) = _configuredProjects.Finalize();
        if (configuredProjectsChanged)
        {
            EnsureCloned();
            newProjectCollection._configuredProjects = configuredProjects!;
        }

        var (syntheticProjects, syntheticProjectsChanged) = _syntheticProjects.Finalize();
        if (syntheticProjectsChanged)
        {
            EnsureCloned();
            newProjectCollection._syntheticProjects = syntheticProjects!;
        }

        if (_openFilesChanged)
        {
            EnsureCloned();
            newProjectCollection._openFiles = ProjectCollection.OpenFilePaths(_overlays);
        }

        if (!MapsEqual(_fileDefaultProjects, _base._fileDefaultProjects))
        {
            EnsureCloned();
            newProjectCollection._fileDefaultProjects = _fileDefaultProjects;
        }

        var (newInferredProject, inferredProjectChanged) = _inferredProject.Finalize();
        if (inferredProjectChanged)
        {
            EnsureCloned();
            newProjectCollection._inferredProject = newInferredProject;
        }

        var configFileRegistry = _configFileRegistryBuilder.Finalize();
        if (!ReferenceEquals(configFileRegistry, _base._configFileRegistry))
        {
            EnsureCloned();
            newProjectCollection._configFileRegistry = configFileRegistry;
        }

        if (!_apiState.Equals(_base._apiState))
        {
            EnsureCloned();
            newProjectCollection._apiState = _apiState;
        }

        return (newProjectCollection, configFileRegistry);
    }

    private void ForEachProject(Func<Dirty.Value<Project>, bool> fn)
    {
        var keepGoing = true;
        _configuredProjects.Range(entry =>
        {
            keepGoing = fn(entry);
            return keepGoing;
        });
        if (keepGoing)
        {
            _syntheticProjects.Range(entry =>
            {
                keepGoing = fn(entry);
                return keepGoing;
            });
        }

        if (!keepGoing)
        {
            return;
        }

        if (_inferredProject.Value() is not null)
        {
            fn(_inferredProject);
        }
    }

    internal Exception? HandleAPIRequest(APISnapshotRequest apiRequest, LogTree? logger)
    {
        Collections.Set<Path>? projectsToClose = null;
        if (apiRequest.CloseProjects is not null)
        {
            foreach (var projectPath in apiRequest.CloseProjects.Keys())
            {
                if (_apiState._openProjects.TryGetValue(projectPath, out var count) && count > 1)
                {
                    _apiState._openProjects[projectPath] = count - 1;
                }
                else if (count == 1)
                {
                    _apiState._openProjects.Remove(projectPath);
                    projectsToClose ??= new Collections.Set<Path>();
                    projectsToClose.Add(projectPath);
                }
            }
        }

        if (apiRequest.OpenProjects is not null)
        {
            foreach (var configFileName in apiRequest.OpenProjects.Keys())
            {
                var configPath = _toPath(configFileName);
                var entry = FindOrCreateProject(configFileName, configPath, ProjectLoadKindCreate, logger);
                if (entry is not null)
                {
                    if (!_apiState._openProjects.TryGetValue(configPath, out var openCount))
                    {
                        openCount = 0;
                    }

                    _apiState._openProjects[configPath] = openCount + 1;

                    // A project re-opened in the same request shouldn't be closed.
                    projectsToClose?.Delete(configPath);
                    UpdateProgram(entry, logger);
                }
                else
                {
                    return new Exception($"project not found for open: {configFileName}");
                }
            }
        }

        if (apiRequest.CloseFiles is not null)
        {
            foreach (var path in apiRequest.CloseFiles.Keys())
            {
                if (_apiState._openFiles.TryGetValue(path, out var entry))
                {
                    if (entry.RefCount > 1)
                    {
                        entry.RefCount--;
                    }
                    else
                    {
                        _apiState._openFiles.Remove(path);
                    }
                }
            }
        }

        if (apiRequest.OpenFiles is not null)
        {
            foreach (var (path, fileName) in apiRequest.OpenFiles)
            {
                if (!_apiState._openFiles.TryGetValue(path, out var entry))
                {
                    entry = new ApiOpenedFile();
                    _apiState._openFiles[path] = entry;
                }

                entry.FileName = fileName;
                entry.RefCount++;
            }
        }

        foreach (var overlay in _overlays.Values)
        {
            var entry = FindDefaultConfiguredProject(overlay.FileName(), _toPath(overlay.FileName()));
            if (entry is not null)
            {
                projectsToClose?.Delete(entry.Value()._configFilePath);
            }
        }

        if (projectsToClose is not null)
        {
            foreach (var projectPath in projectsToClose.Keys())
            {
                var (entry, ok) = _configuredProjects.Load(new ConfiguredProjectID(projectPath));
                if (ok)
                {
                    DeleteProject(entry!, logger);
                }
            }
        }

        // Place newly API-opened files like LSP's textDocument/didOpen, ensuring only
        // their target projects. Existing API-opened files are retained by cleanup below
        // without implicitly updating their programs.
        if (apiRequest.OpenFiles is not null)
        {
            var retain = new Collections.Set<Path>();
            var ensureInferredProject = false;
            foreach (var (path, fileName) in apiRequest.OpenFiles)
            {
                if (IsOpenFile(path))
                {
                    if (FindDefaultConfiguredProject(fileName, path) is null && !IsSupportedInInferredProject(fileName))
                    {
                        return new Exception($"no project found for opened file: {fileName}");
                    }

                    continue;
                }

                var result = EnsureConfiguredProjectAndAncestorsForFile(fileName, path, logger);
                retain.Union(result._retain);
                if (result._project is null)
                {
                    if (!IsSupportedInInferredProject(fileName))
                    {
                        return new Exception($"no project found for opened file: {fileName}");
                    }

                    ensureInferredProject = true;
                }
            }

            CleanupConfiguredProjects(retain, logger);
            if (ensureInferredProject && _inferredProject.Value() is not null)
            {
                UpdateProgram(_inferredProject, logger);
            }
        }
        else if (apiRequest.CloseFiles is not null)
        {
            CleanupConfiguredProjects(null, logger);
        }

        var seenReconfiguredPrograms = new Collections.Set<SyntheticProjectID>();
        var reconfigurePrograms = apiRequest.ReconfigurePrograms ?? [];
        foreach (var request in reconfigurePrograms)
        {
            if (seenReconfiguredPrograms.Has(request.ProgramID))
            {
                return new Exception($"synthetic program reconfigured more than once: {request.ProgramID}");
            }

            seenReconfiguredPrograms.Add(request.ProgramID);
            if (apiRequest.RemovePrograms?.Has(request.ProgramID) ?? false)
            {
                return new Exception($"synthetic program cannot be reconfigured and removed: {request.ProgramID}");
            }

            if (!_syntheticProjects.Load(request.ProgramID).Ok)
            {
                return new Exception($"synthetic program not found for reconfiguration: {request.ProgramID}");
            }
        }

        if (apiRequest.RemovePrograms is not null)
        {
            foreach (var programID in apiRequest.RemovePrograms.Keys())
            {
                var (project, ok) = _syntheticProjects.Load(programID);
                if (!ok)
                {
                    return new Exception($"synthetic program not found for removal: {programID}");
                }

                DeleteProject(project!, logger);
            }
        }

        var createPrograms = apiRequest.CreatePrograms ?? [];
        var createdPrograms = new Project[createPrograms.Length];
        var createdEntries = new Dirty.Value<Project>[createPrograms.Length];
        for (var i = 0; i < createPrograms.Length; i++)
        {
            var request = createPrograms[i];
            var entry = UpdateOrCreateSyntheticProject(
                NextSyntheticProjectID(),
                [.. request.RootFileNames],
                request.CompilerOptions,
                request.ProjectReferences,
                request.ConfigFileParsingDiagnostics,
                request.ModuleResolverFactory,
                request.ModuleResolverID,
                _inferredContentMappers,
                logger);
            createdEntries[i] = entry;
        }

        var reconfiguredEntries = new Dirty.Value<Project>[reconfigurePrograms.Length];
        for (var i = 0; i < reconfigurePrograms.Length; i++)
        {
            var request = reconfigurePrograms[i];
            reconfiguredEntries[i] = UpdateOrCreateSyntheticProject(
                request.ProgramID,
                [.. request.RootFileNames],
                request.CompilerOptions,
                request.ProjectReferences,
                request.ConfigFileParsingDiagnostics,
                request.ModuleResolverFactory,
                request.ModuleResolverID,
                _inferredContentMappers,
                logger);
        }

        var wg = NewWorkGroup(singleThreaded: false);
        for (var i = 0; i < createdEntries.Length; i++)
        {
            var index = i;
            var entry = createdEntries[i];
            wg.Queue(() =>
            {
                if (entry.Value()._dirty)
                {
                    UpdateProgram(entry, logger);
                }

                createdPrograms[index] = entry.Value();
            });
        }

        foreach (var entry in reconfiguredEntries)
        {
            var e = entry;
            wg.Queue(() =>
            {
                if (e.Value()._dirty)
                {
                    UpdateProgram(e, logger);
                }
            });
        }

        wg.RunAndWait();
        _createdPrograms = createdPrograms;
        foreach (var (path, fileName) in apiRequest.EnsureFiles ?? [])
        {
            DidRequestFile(fileName, path, configuredProjectsOnly: false, logger);
            if (FindDefaultProject(fileName, path) is null)
            {
                return new Exception($"no project found for opened file: {fileName}");
            }
        }

        if (apiRequest.EnsurePrograms is not null)
        {
            foreach (var projectID in apiRequest.EnsurePrograms.Keys())
            {
                DidRequestProject(projectID, logger);
            }
        }

        if (apiRequest.EnsureAllPrograms)
        {
            ForEachProject(entry =>
            {
                UpdateProgram(entry, logger);
                return true;
            });
        }

        Exception? moduleResolutionError = null;
        ForEachProject(entry =>
        {
            var project = entry.Value();
            if (project.Program is not null)
            {
                moduleResolutionError = project.Program.ModuleResolutionError();
            }

            return moduleResolutionError is null;
        });
        return moduleResolutionError;
    }

    private SyntheticProjectID NextSyntheticProjectID()
    {
        for (var id = 1; ; id++)
        {
            var projectID = SyntheticProjectID.NewSyntheticProjectID(id);
            if (!_syntheticProjects.Load(projectID).Ok)
            {
                return projectID;
            }
        }
    }

    internal void DidChangeFiles(FileChangeSummary summary, LogTree? logger)
    {
        _openFilesChanged = _openFilesChanged || summary.Opened != default || summary.Closed.Len() > 0;

        Path[] ToPaths(Collections.Set<DocumentUri> uris)
        {
            var paths = new Path[uris.Len()];
            var i = 0;
            foreach (var uri in uris.Keys())
            {
                paths[i++] = _toPath(uri.FileName());
            }

            return paths;
        }

        var changedFiles = ToPaths(summary.Changed);
        var deletedFiles = ToPaths(summary.Deleted);
        var createdFiles = ToPaths(summary.Created);
        if (_contentMapperHost is not null)
        {
            var allWatchChanges = new Path[changedFiles.Length + deletedFiles.Length + createdFiles.Length];
            changedFiles.CopyTo(allWatchChanges, 0);
            deletedFiles.CopyTo(allWatchChanges, changedFiles.Length);
            createdFiles.CopyTo(allWatchChanges, changedFiles.Length + deletedFiles.Length);
            ForEachProject(entry =>
            {
                RefreshContentMapperProjectForChanges(entry, allWatchChanges, summary.HasExcessiveNonCreateWatchEvents(), logger);
                return true;
            });
        }

        var configChangeLogger = logger?.Fork("Checking for changes affecting config files");
        var configChangeResult = _configFileRegistryBuilder.DidChangeFiles(summary, configChangeLogger);
        LogChangeFileResult(configChangeResult, configChangeLogger);

        _programStructureChanged = MarkProjectsAffectedByConfigChanges(configChangeResult, logger);

        ForEachProject(entry =>
        {
            // Only consider change/delete; creates are handled by the config file registry
            if (summary.HasExcessiveNonCreateWatchEvents())
            {
                entry.Change(p =>
                {
                    p._dirty = true;
                    p._dirtyFilePath = default;
                    logger?.Logf("Marking project as dirty due to excessive watch changes: {0}", p.ID().Value);
                });
                return true;
            }

            // Handle closed and changed files
            MarkFilesChanged(entry, changedFiles, FileChangeType.Changed, logger);
            if (entry.Value().Kind == KindInferred && summary.Closed.Len() > 0)
            {
                var rootFilesMap = entry.Value().CommandLine!.FileNamesByPath();
                var newRootFiles = (string[])entry.Value().CommandLine!.FileNames().Clone();
                foreach (var uri in summary.Closed.Keys())
                {
                    var fileName = uri.FileName();
                    var path = _toPath(fileName);
                    if (rootFilesMap.ContainsKey(path))
                    {
                        newRootFiles = DeleteFirst(newRootFiles, fileName);
                    }
                }

                UpdateInferredProjectRoots(newRootFiles, logger);
            }

            // Handle deleted files
            if (summary.Deleted.Len() > 0)
            {
                MarkFilesChanged(entry, deletedFiles, FileChangeType.Deleted, logger);
            }

            // Handle created files
            if (summary.Created.Len() > 0)
            {
                MarkFilesChanged(entry, createdFiles, FileChangeType.Created, logger);
            }

            return true;
        });

        // Handle opened file
        if (summary.Opened != default || summary.Reopened != default)
        {
            var fileName = FirstNonZero(summary.Opened, summary.Reopened).FileName();
            var path = _toPath(fileName);
            var openFileResult = EnsureConfiguredProjectAndAncestorsForFile(fileName, path, logger);
            CleanupConfiguredProjects(openFileResult._retain, logger);
        }
    }

    private void RefreshContentMapperProjectForChanges(Dirty.Value<Project> entry, Path[] paths, bool refreshAll, LogTree? logger)
    {
        var project = entry.Value();
        if (project.Program is null || project._contentMapperWatchedFiles is null)
        {
            return;
        }

        var affected = refreshAll;
        if (!affected)
        {
            if (Array.Exists(paths, project._contentMapperWatchedFiles.Has))
            {
                affected = true;
            }
        }

        if (!affected)
        {
            return;
        }

        var contentMapperProject = project.Program.ContentMapperProject();
        contentMapperProject?.Refresh();
        entry.Change(p =>
        {
            p._dirty = true;
            p._dirtyFilePath = default;
            logger?.Logf("Marking project as dirty due to content mapper configuration changes: {0}", p.ID().Value);
        });
    }

    /// <summary>
    /// cleanupConfiguredProjects sweeps the loaded configured projects and unloads those
    /// that are no longer needed. Starting from the set of all configured projects, it
    /// retains any project that is the default project (along with its references and
    /// ancestor configs) of an open overlay file or an API-opened file, any project
    /// explicitly opened through the API, and any project in retain (e.g. the ancestor
    /// solution tree built for a freshly opened overlay file). Every other configured
    /// project is deleted, the inferred project roots are recomputed, and the config file
    /// registry is cleaned up. This is the shared mechanism that keeps the set of loaded
    /// projects minimal for both LSP file opens and API file opens/closes.
    /// </summary>
    private void CleanupConfiguredProjects(Collections.Set<Path>? retain, LogTree? logger)
    {
        var toRemoveProjects = new Collections.Set<Path>();
        _configuredProjects.Range(entry =>
        {
            toRemoveProjects.Add(entry.Key().Value);
            return true;
        });

        void RetainConfiguredProjectAndReferences(Project project)
        {
            // Retain project
            toRemoveProjects.Delete(project.ConfigFilePath());
            var program = project.GetProgram();
            if (program is not null)
            {
                program.RangeResolvedProjectReference((referencePath, _, _, _) =>
                {
                    if (_configuredProjects.Load(new ConfiguredProjectID(referencePath)).Ok)
                    {
                        toRemoveProjects.Delete(referencePath);
                    }

                    return true;
                });
            }
        }

        void RetainDefaultConfiguredProject(Path openFilePath, Project project)
        {
            // Retain project and its references
            RetainConfiguredProjectAndReferences(project);

            // Retain all the ancestor projects
            _configFileRegistryBuilder.ForEachConfigFileNameFor(openFilePath, configFileName =>
            {
                var ancestor = FindOrCreateProject(configFileName, _toPath(configFileName), ProjectLoadKindFind, logger);
                if (ancestor is not null)
                {
                    RetainConfiguredProjectAndReferences(ancestor.Value());
                }
            });
        }

        var inferredProjectFiles = new List<string>();
        foreach (var overlay in _overlays.Values)
        {
            var openFile = overlay.FileName();
            var openFilePath = _toPath(openFile);
            var p = FindDefaultConfiguredProject(openFile, openFilePath);
            if (p is not null)
            {
                RetainDefaultConfiguredProject(openFilePath, p.Value());
            }
            else
            {
                inferredProjectFiles.Add(openFile);
            }
        }

        // Treat API-opened files like open files: retain their configured project (so
        // an LSP-driven open doesn't close it), or keep them as inferred project roots.
        foreach (var (path, file) in _apiState._openFiles)
        {
            if (IsOpenFile(path))
            {
                continue;
            }

            var p = FindDefaultConfiguredProject(file.FileName, path);
            if (p is not null)
            {
                RetainDefaultConfiguredProject(path, p.Value());
            }
            else
            {
                inferredProjectFiles.Add(file.FileName);
            }
        }

        foreach (var projectPath in toRemoveProjects.Keys())
        {
            if (retain is not null && retain.Has(projectPath))
            {
                continue;
            }

            if (_apiState._openProjects.ContainsKey(projectPath))
            {
                continue;
            }

            var (p, ok) = _configuredProjects.Load(new ConfiguredProjectID(projectPath));
            if (ok)
            {
                DeleteProject(p!, logger);
            }
        }

        UpdateInferredProjectRoots(inferredProjectFiles.ToArray(), logger);
        _configFileRegistryBuilder.Cleanup();
    }

    /// <summary>cleanupAllConfiguredProjects removes all configured projects unconditionally。</summary>
    private void CleanupAllConfiguredProjects(LogTree? logger)
    {
        _configuredProjects.Range(entry =>
        {
            var (p, ok) = _configuredProjects.Load(entry.Key());
            if (ok)
            {
                DeleteProject(p!, logger);
            }

            return true;
        });
        _configFileRegistryBuilder.Cleanup();
    }

    /// <summary>Go: <c>func logChangeFileResult(result changeFileResult, logger *logging.LogTree)</c>（见文件头第 1 条）。</summary>
    internal static void LogChangeFileResult(ChangeFileResult result, LogTree? logger)
    {
        if ((result._affectedProjects?.Len() ?? 0) > 0)
        {
            logger?.Logf("Config file change affected projects: {0}", FormatSet(result._affectedProjects!));
        }

        if ((result._affectedFiles?.Len() ?? 0) > 0)
        {
            logger?.Logf("Config file change affected config file lookups for {0} files", result._affectedFiles!.Len());
        }
    }

    private string[] CollectInferredProjectRoots()
    {
        var inferredProjectFiles = new List<string>();
        foreach (var (path, overlay) in _overlays)
        {
            if (FindDefaultConfiguredProject(overlay.FileName(), path) is null)
            {
                inferredProjectFiles.Add(overlay.FileName());
            }
        }

        return AppendAPIOpenedInferredRoots(inferredProjectFiles);
    }

    /// <summary>
    /// appendAPIOpenedInferredRoots appends API-opened files that aren't open in an
    /// overlay and have no configured project, so they're kept as inferred project
    /// roots and persist across snapshots.
    /// </summary>
    private string[] AppendAPIOpenedInferredRoots(List<string> inferredProjectFiles)
    {
        foreach (var (path, file) in _apiState._openFiles)
        {
            if (IsOpenFile(path))
            {
                continue;
            }

            if (FindDefaultConfiguredProject(file.FileName, path) is null)
            {
                inferredProjectFiles.Add(file.FileName);
            }
        }

        return inferredProjectFiles.ToArray();
    }

    private void CleanupInferredProject(LogTree? logger)
    {
        UpdateInferredProjectRoots(CollectInferredProjectRoots(), logger);
    }

    internal void DidChangeContentMapperContributions(LogTree? logger)
    {
        CleanupInferredProject(logger);
        if (_inferredProject.Value() is not null)
        {
            UpdateProgram(_inferredProject, logger);
        }
    }

    private void EnsureInferredProjectIncludesClosedFile(string fileName, LogTree? logger)
    {
        // Collect existing inferred project roots (open files not in configured projects)
        // plus this closed file.
        var inferredProjectFiles = new List<string>(CollectInferredProjectRoots()) { fileName };
        UpdateInferredProjectRoots(inferredProjectFiles.ToArray(), logger);
        if (_inferredProject.Value() is not null)
        {
            UpdateProgram(_inferredProject, logger);
        }
    }

    /// <summary>
    /// DidRequestFile ensures projects are loaded for the given URI.
    /// If configuredProjectsOnly is true, only configured projects are loaded; no inferred project is created
    /// and it is not guaranteed that there will be any project containing the file in the resulting snapshot.
    /// </summary>
    internal void DidRequestFile(DocumentUri uri, bool configuredProjectsOnly, LogTree? logger)
    {
        var fileName = uri.FileName();
        var path = _toPath(fileName);
        DidRequestFile(fileName, path, configuredProjectsOnly, logger);
    }

    private void DidRequestFile(string fileName, Path path, bool configuredProjectsOnly, LogTree? logger)
    {
        var startTime = Stopwatch.GetTimestamp();
        if (_defaultProjectsInvalidated)
        {
            EnsureConfiguredProjectAndAncestorsForFile(fileName, path, logger);
            if (!IsOpenFile(path))
            {
                return;
            }
        }

        if (IsOpenFile(path))
        {
            var hasChanges = _programStructureChanged;

            // See if we can find a default project without updating a bunch of stuff.
            var result = FindDefaultProject(fileName, path);
            if (result is not null)
            {
                hasChanges = UpdateProgram(result, logger) || hasChanges;
                if (result.Value() is not null && result.Value().ContainsFile(path))
                {
                    if (hasChanges)
                    {
                        CleanupInferredProject(logger);
                        if (_inferredProject.Value() is not null)
                        {
                            UpdateProgram(_inferredProject, logger);
                        }
                    }

                    return;
                }
            }

            // Make sure all projects we know about are up to date...
            _configuredProjects.Range(entry =>
            {
                hasChanges = UpdateProgram(entry, logger) || hasChanges;
                return true;
            });
            if (hasChanges)
            {
                // If the structure of other projects changed, we might need to move files
                // in/out of the inferred project.
                CleanupInferredProject(logger);
            }

            if (_inferredProject.Value() is not null)
            {
                UpdateProgram(_inferredProject, logger);
            }

            // At this point we should be able to find the default project for the file without
            // creating anything else. Initially, I verified that and panicked if nothing was found,
            // but that panic was getting triggered by fourslash infrastructure when it told us to
            // open a package.json file. This is something the VS Code client would never do, but
            // it seems possible that another client would. There's no point in panicking; we don't
            // really even have an error condition until it tries to ask us language questions about
            // a non-TS-handleable file.
        }
        else
        {
            var result = EnsureConfiguredProjectAndAncestorsForFile(fileName, path, logger);
            if (result._project is null && !configuredProjectsOnly)
            {
                // No configured project found for this closed file.
                // Add it to the inferred project so language service requests can be served.
                EnsureInferredProjectIncludesClosedFile(fileName, logger);
            }
        }

        logger?.Logf("Completed file request for {0} in {1}", fileName, Stopwatch.GetElapsedTime(startTime));
    }

    internal void DidRequestProject(ID projectID, LogTree? logger)
    {
        var startTime = Stopwatch.GetTimestamp();
        if (projectID.TryInferred(out _))
        {
            // Update inferred project
            if (_inferredProject.Value() is not null)
            {
                UpdateProgram(_inferredProject, logger);
            }
        }
        else if (projectID.TrySynthetic(out var syntheticID))
        {
            var (entry, ok) = _syntheticProjects.Load(syntheticID);
            if (ok)
            {
                UpdateProgram(entry!, logger);
            }
        }
        else if (projectID.TryConfigured(out var configuredID))
        {
            var (entry, ok) = _configuredProjects.Load(configuredID);
            if (ok)
            {
                UpdateProgram(entry!, logger);
            }
        }

        logger?.Logf("Completed project update request for {0} in {1}", projectID, Stopwatch.GetElapsedTime(startTime));
    }

    internal void DidRequestProjectTrees(ProjectTreeRequest projectTreeRequest, LogTree? logger)
    {
        var startTime = Stopwatch.GetTimestamp();

        var currentProjects = new List<ConfiguredProjectID>();
        _configuredProjects.Range(sme =>
        {
            currentProjects.Add(sme.Key());
            return true;
        });

        var seenProjects = new SyncSet<ConfiguredProjectID>();
        var wg = NewWorkGroup(singleThreaded: false);
        foreach (var projectId in currentProjects)
        {
            var id = projectId;
            wg.Queue(() =>
            {
                var (entry, ok) = _configuredProjects.Load(id);
                if (ok)
                {
                    // If this project has potential project reference for any of the project we are loading ancestor tree for
                    // load this project first
                    var project = entry!.Value();
                    if (project is not null && (projectTreeRequest.IsAllProjects() || project.HasPotentialProjectReference(projectTreeRequest)))
                    {
                        UpdateProgram(entry!, logger);
                    }

                    EnsureProjectTree(wg, entry!, projectTreeRequest, seenProjects, logger);
                }
            });
        }

        wg.RunAndWait();

        logger?.Logf("Completed project tree request for {0} in {1}", joinPaths(projectTreeRequest.Projects()), Stopwatch.GetElapsedTime(startTime));

        static string joinPaths(Path[]? paths)
        {
            return "[" + string.Join(" ", System.Array.ConvertAll(paths ?? [], static p => p.Value)) + "]";
        }
    }

    private void EnsureProjectTree(
        WorkGroup wg,
        Dirty.SyncMapEntry<ConfiguredProjectID, Project> entry,
        ProjectTreeRequest projectTreeRequest,
        SyncSet<ConfiguredProjectID> seenProjects,
        LogTree? logger)
    {
        if (!seenProjects.AddIfAbsent(entry.Key()))
        {
            return;
        }

        var project = entry.Value();
        if (project is null)
        {
            return;
        }

        var program = project.GetProgram();
        if (program is null)
        {
            return;
        }

        // If this project disables child load ignore it
        if (program.CommandLine().CompilerOptions()!.DisableReferencedProjectLoad.IsTrue())
        {
            return;
        }

        var children = program.GetResolvedProjectReferences();
        if (children is null)
        {
            return;
        }

        foreach (var childConfig in children)
        {
            if (childConfig is null)
            {
                continue;
            }

            var child = childConfig;
            wg.Queue(() =>
            {
                if (!projectTreeRequest.IsAllProjects() && program.RangeResolvedProjectReferenceInChildConfig(
                    child,
                    (referencePath, _, _, _) => !projectTreeRequest.IsProjectReferenced(referencePath)))
                {
                    return;
                }

                // Load this child project since this is referenced
                var childProjectEntry = FindOrCreateProject(child.ConfigName(), child.ConfigFile!.SourceFile.Path(), ProjectLoadKindCreate, logger);
                UpdateProgram(childProjectEntry!, logger);

                // Ensure children for this project
                EnsureProjectTree(wg, childProjectEntry!, projectTreeRequest, seenProjects, logger);
            });
        }
    }

    internal void DidUpdateATAState(Dictionary<ID, ATAStateChange> ataChanges, LogTree? logger)
    {
        void UpdateProject(Dirty.Value<Project> project, ATAStateChange ataChange)
        {
            project.ChangeIf(
                p =>
                {
                    if (p is null)
                    {
                        return false;
                    }

                    // Consistency check: the ATA demands (project options, unresolved imports) of this project
                    // has not changed since the time the ATA request was dispatched; the change can still be
                    // applied to this project in its current state.
                    return ataChange.TypingsInfo!.Equals(p.ComputeTypingsInfo());
                },
                p =>
                {
                    // We checked before triggering this change (in Session.triggerATAForUpdatedProjects) that
                    // the set of typings files is actually different.
                    p._installedTypingsInfo = ataChange.TypingsInfo;
                    p._typingsFiles = ataChange.TypingsFiles;
                    var typingsWatchGlobs = Watch.GetTypingsLocationsGlobs(
                        ataChange.TypingsFilesToWatch,
                        _sessionOptions.TypingsLocation,
                        _sessionOptions.CurrentDirectory,
                        p._currentDirectory,
                        _fs._fs.UseCaseSensitiveFileNames());
                    p._typingsWatch = p._typingsWatch?.Clone(typingsWatchGlobs);
                    p._dirty = true;
                    p._dirtyFilePath = default;
                });
        }

        foreach (var (projectID, ataChange) in ataChanges)
        {
            if (ataChange.Logs is not null)
            {
                logger?.Embed(ataChange.Logs);
            }

            if (projectID.TryInferred(out _))
            {
                UpdateProject(_inferredProject, ataChange);
            }
            else if (projectID.TrySynthetic(out var syntheticProjectID))
            {
                var (project, loaded) = _syntheticProjects.Load(syntheticProjectID);
                if (loaded)
                {
                    UpdateProject(project!, ataChange);
                }
            }
            else if (projectID.TryConfigured(out var configuredID))
            {
                var (project, loaded) = _configuredProjects.Load(configuredID);
                if (loaded)
                {
                    UpdateProject(project!, ataChange);
                }
            }

            logger?.Logf("Updated ATA state for project {0}", projectID);
        }
    }

    /// <summary>if customConfigFileName changes, invalidate default projects。</summary>
    internal void DidChangeCustomConfigFileName(LogTree? logger)
    {
        if (!_configFileRegistryBuilder.DidChangeCustomConfigFileName(logger))
        {
            return;
        }

        // Go: b.fileDefaultProjects = nil（C# 的字段是 Dictionary ⇒ 清空等价，见文件头第 14 条）
        _fileDefaultProjects.Clear();
        _defaultProjectsInvalidated = true;
        _programStructureChanged = true;
    }

    internal void DidChangeUserPreferences(LsUtil.UserPreferences oldPreferences, LsUtil.UserPreferences newPreferences, LogTree? logger)
    {
        if (oldPreferences.Locale == newPreferences.Locale)
        {
            return;
        }

        ForEachProject(entry =>
        {
            entry.Change(project =>
            {
                project._dirty = true;
                project._dirtyFilePath = default;
                logger?.Logf("Marking project as dirty due to locale change: {0}", project.ID().Value);
            });
            return true;
        });
    }

    private bool MarkProjectsAffectedByConfigChanges(ChangeFileResult configChangeResult, LogTree? logger)
    {
        if (configChangeResult._affectedProjects is not null)
        {
            foreach (var projectID in configChangeResult._affectedProjects.M)
            {
                Dirty.Value<Project>? project = null;
                if (projectID.TryInferred(out _))
                {
                    project = _inferredProject;
                }
                else
                {
                    if (projectID.TrySynthetic(out var syntheticProjectID))
                    {
                        var (syntheticProject, loaded) = _syntheticProjects.Load(syntheticProjectID);
                        if (loaded)
                        {
                            project = syntheticProject;
                        }
                    }

                    if (project is null && projectID.TryConfigured(out var configuredID))
                    {
                        project = _configuredProjects.Load(configuredID).Entry;
                    }
                }

                if (project is null || project.Value() is null)
                {
                    Debug.Fail($"project {projectID.Value} affected by config change not found");
                    continue;
                }

                project.ChangeIf(
                    p => !p._dirty || p._dirtyFilePath != default,
                    p =>
                    {
                        p._dirty = true;
                        p._dirtyFilePath = default;
                        logger?.Logf("Marking project {0} as dirty due to change affecting config", projectID.Value);
                    });
            }
        }

        // Recompute default projects for open files that now have different config file presence.
        var hasChanges = false;
        var affectedFiles = configChangeResult._affectedFiles;
        if (affectedFiles is not null)
        {
            foreach (var path in affectedFiles.M)
            {
                if (!_overlays.TryGetValue(path, out var overlay))
                {
                    continue;
                }

                var fileName = overlay.FileName();
                EnsureConfiguredProjectAndAncestorsForFile(fileName, path, logger);
                hasChanges = true;
            }
        }

        return hasChanges;
    }

    private Dirty.Value<Project>? FindDefaultProject(string fileName, Path path)
    {
        var configuredProject = FindDefaultConfiguredProject(fileName, path);
        if (configuredProject is not null)
        {
            return configuredProject;
        }

        if (_fileDefaultProjects.TryGetValue(path, out var key))
        {
            if (key.TryInferred(out _))
            {
                return _inferredProject;
            }
        }

        var inferredProject = _inferredProject.Value();
        if (inferredProject is not null && inferredProject.ContainsFile(path))
        {
            _fileDefaultProjects[path] = InferredProjectID.InferredProjectIDConst.AsID();
            return _inferredProject;
        }

        return null;
    }

    private Dirty.SyncMapEntry<ConfiguredProjectID, Project>? FindDefaultConfiguredProject(string fileName, Path path)
    {
        if (_fileDefaultProjects.TryGetValue(path, out var key) && key.TryConfigured(out var configuredID))
        {
            var (entry, ok) = _configuredProjects.Load(configuredID);
            if (ok)
            {
                return entry;
            }
        }

        // Sort configured projects so we can use a deterministic "first" as a last resort.
        var configuredProjectPaths = new List<Path>();
        var configuredProjects = new Dictionary<Path, Dirty.SyncMapEntry<ConfiguredProjectID, Project>>();
        _configuredProjects.Range(entry =>
        {
            var configuredPath = entry.Key().Value;
            configuredProjectPaths.Add(configuredPath);
            configuredProjects[configuredPath] = entry;
            return true;
        });
        configuredProjectPaths.Sort(static (a, b) => string.CompareOrdinal(a.Value, b.Value));

        var (project, multipleCandidates) = ProjectCollection.FindDefaultConfiguredProjectFromProgramInclusion(
            fileName,
            path,
            configuredProjectPaths.ToArray(),
            p => configuredProjects[p].Value());

        if (multipleCandidates)
        {
            var p = FindOrCreateDefaultConfiguredProjectForFile(fileName, path, ProjectLoadKindFind, logger: null)._project;
            if (p is not null)
            {
                return p;
            }
        }

        return configuredProjects.GetValueOrDefault(project);
    }

    private SearchResult EnsureConfiguredProjectAndAncestorsForFile(string fileName, Path path, LogTree? logger)
    {
        var result = FindOrCreateDefaultConfiguredProjectForFile(fileName, path, ProjectLoadKindCreate, logger);
        if (result._project is not null && IsOpenFile(path))
        {
            CreateAncestorTree(fileName, path, result, logger);
        }

        return result;
    }

    private void CreateAncestorTree(string fileName, Path path, SearchResult openResult, LogTree? logger)
    {
        var project = openResult._project!.Value();
        while (true)
        {
            // Skip if project is not composite and we are only looking for solution
            if (project.CommandLine is not null
                && (!project.CommandLine.CompilerOptions()!.Composite.IsTrue()
                    || project.CommandLine.CompilerOptions()!.DisableSolutionSearching.IsTrue()))
            {
                return;
            }

            // Get config file name
            var ancestorConfigName = _configFileRegistryBuilder.GetAncestorConfigFileName(fileName, path, project.ConfigFileName(), logger);
            if (ancestorConfigName.Length == 0)
            {
                return;
            }

            // find or delay load the project
            var ancestorPath = _toPath(ancestorConfigName);
            var ancestor = FindOrCreateProject(ancestorConfigName, ancestorPath, ProjectLoadKindCreate, logger);
            if (ancestor is null)
            {
                return;
            }

            openResult._retain.Add(ancestorPath);

            // If this ancestor is new and was not updated because we are just creating it for future loading
            // eg when invoking find all references or rename that could span multiple projects
            // we would make the current project as its potential project reference
            if (ancestor.Value().CommandLine is null
                && (project.CommandLine is null || project.CommandLine.CompilerOptions()!.Composite.IsTrue()))
            {
                ancestor.Change(ancestorProject => ancestorProject.SetPotentialProjectReference(project._configFilePath));
            }

            project = ancestor.Value();
        }
    }

    private SearchResult FindOrCreateDefaultConfiguredProjectWorker(
        string fileName,
        Path path,
        string configFileName,
        ProjectLoadKind loadKind,
        SyncSet<SearchNodeKey>? visited,
        SearchResult? fallback,
        LogTree? logger)
    {
        var configs = new Collections.SyncMap<Path, ParsedCommandLine>();
        visited ??= new SyncSet<SearchNodeKey>();

        var search = BreadthFirstSearchParallelEx<SearchNodeKey, SearchNode>(
            new SearchNode { _configFileName = configFileName, _loadKind = loadKind, _logger = logger },
            node =>
            {
                var (config, ok) = configs.Load(_toPath(node._configFileName));
                if (ok && (config!.ProjectReferences()?.Length ?? 0) > 0)
                {
                    var referenceLoadKind = node._loadKind;
                    if (config.CompilerOptions()!.DisableReferencedProjectLoad.IsTrue())
                    {
                        referenceLoadKind = ProjectLoadKindFind;
                    }

                    LogTree? refLogger = null;
                    var references = config.ResolvedProjectReferencePaths();
                    if (references.Length > 0 && node._logger is not null)
                    {
                        refLogger = node._logger.Fork($"Searching {references.Length} project references of {node._configFileName}");
                    }

                    return Map(references, configFileName => new SearchNode
                    {
                        _configFileName = configFileName,
                        _loadKind = referenceLoadKind,
                        _logger = refLogger?.Fork("Searching project reference " + configFileName),
                    });
                }

                return [];
            },
            node =>
            {
                var configFilePath = _toPath(node._configFileName);
                var config = _configFileRegistryBuilder.FindOrAcquireConfigForFile(
                    node._configFileName,
                    configFilePath,
                    path,
                    node._loadKind,
                    node._logger?.Fork("Acquiring config for open file"));
                if (config is null)
                {
                    node._logger?.Log("Config file for project does not already exist");
                    return (false, false);
                }

                configs.Store(configFilePath, config);
                if (config.FileNames().Length == 0)
                {
                    // Likely a solution tsconfig.json - the search will fan out to its references.
                    node._logger?.Log("Project does not contain file (no root files)");
                    return (false, false);
                }

                if (config.CompilerOptions()!.Composite == Tristate.TSTrue)
                {
                    // For composite projects, we can get an early negative result.
                    // !!! what about declaration files in node_modules? wouldn't it be better to
                    //     check project inclusion if the project is already loaded?
                    if (!config.FileNamesByPath().ContainsKey(path))
                    {
                        node._logger?.Log("Project does not contain file (by composite config inclusion)");
                        return (false, false);
                    }
                }

                var project = FindOrCreateProject(node._configFileName, configFilePath, node._loadKind, node._logger);
                if (project is null)
                {
                    node._logger?.Log("Project does not already exist");
                    return (false, false);
                }

                if (node._loadKind == ProjectLoadKindCreate)
                {
                    // Ensure project is up to date before checking for file inclusion
                    UpdateProgram(project, node._logger);
                }

                if (project.Value().ContainsFile(path))
                {
                    var isDirectInclusion = !project.Value().IsSourceFromProjectReference(path);
                    node._logger?.Logf(
                        "Project contains file {0}",
                        IfElse(isDirectInclusion, "directly", "as a source of a referenced project"));
                    return (true, isDirectInclusion);
                }

                node._logger?.Log("Project does not contain file");
                return (false, false);
            },
            new BreadthFirstSearchOptions<SearchNodeKey, SearchNode>
            {
                Visited = visited,
                PreprocessLevel = level =>
                {
                    level.Range(node =>
                    {
                        if (node._loadKind == ProjectLoadKindFind
                            && level.Has(new SearchNodeKey(node._configFileName, ProjectLoadKindCreate)))
                        {
                            // Remove find requests when a create request for the same project is already present.
                            level.Delete(new SearchNodeKey(node._configFileName, node._loadKind));
                        }

                        return true;
                    });
                },
            },
            node => new SearchNodeKey(node._configFileName, node._loadKind));

        var retain = new Collections.Set<Path>();
        Dirty.SyncMapEntry<ConfiguredProjectID, Project>? project = null;
        if (search.Path is { Length: > 0 } searchPath)
        {
            var (entry, _) = _configuredProjects.Load(new ConfiguredProjectID(_toPath(searchPath[0]._configFileName)));
            project = entry;

            // If we found a project, we retain each project along the BFS path.
            // We don't want to retain everything we visited since BFS can terminate
            // early, and we don't want to retain nondeterministically.
            foreach (var node in searchPath)
            {
                retain.Add(_toPath(node._configFileName));
            }
        }

        if (search.Stopped)
        {
            // Found a project that directly contains the file.
            return new SearchResult { _project = project, _retain = retain };
        }

        if (project is not null)
        {
            // If we found a project that contains the file, but it is a source from
            // a project reference, record it as a fallback.
            fallback = new SearchResult { _project = project, _retain = retain };
        }

        // Look for tsconfig.json files higher up the directory tree and do the same. This handles
        // the common case where a higher-level "solution" tsconfig.json contains all projects in a
        // workspace.
        var (ownConfig, ownConfigOk) = configs.Load(_toPath(configFileName));
        if (ownConfigOk && ownConfig!.CompilerOptions()!.DisableSolutionSearching.IsTrue())
        {
            if (fallback is not null)
            {
                return fallback;
            }
        }

        var ancestorConfigName = _configFileRegistryBuilder.GetAncestorConfigFileName(fileName, path, configFileName, logger);
        if (ancestorConfigName.Length != 0)
        {
            return FindOrCreateDefaultConfiguredProjectWorker(
                fileName,
                path,
                ancestorConfigName,
                loadKind,
                visited,
                fallback,
                logger?.Fork("Searching ancestor config file at " + ancestorConfigName));
        }

        if (fallback is not null)
        {
            return fallback;
        }

        // If we didn't find anything, we can retain everything we visited,
        // since the whole graph must have been traversed (i.e., the set of
        // retained projects is guaranteed to be deterministic).
        visited.Range(node =>
        {
            retain.Add(_toPath(node.ConfigFileName));
            return true;
        });
        return new SearchResult { _retain = retain };
    }

    private SearchResult FindOrCreateDefaultConfiguredProjectForFile(
        string fileName,
        Path path,
        ProjectLoadKind loadKind,
        LogTree? logger)
    {
        if (_fileDefaultProjects.TryGetValue(path, out var key))
        {
            if (key.TryInferred(out _))
            {
                // The file belongs to the inferred project
                return new SearchResult();
            }

            key.TryConfigured(out var configuredID);
            var (entry, _) = _configuredProjects.Load(configuredID);
            return new SearchResult { _project = entry };
        }

        var configFileName = _configFileRegistryBuilder.GetConfigFileNameForFile(fileName, path, logger);
        if (configFileName.Length != 0)
        {
            var startTime = Stopwatch.GetTimestamp();
            var result = FindOrCreateDefaultConfiguredProjectWorker(
                fileName,
                path,
                configFileName,
                loadKind,
                visited: null,
                fallback: null,
                logger?.Fork("Searching for default configured project for " + fileName));
            if (result._project is not null)
            {
                _fileDefaultProjects[path] = result._project.Value().ID();
            }

            if (logger is not null)
            {
                var elapsed = Stopwatch.GetElapsedTime(startTime);
                if (result._project is not null)
                {
                    logger.Logf(
                        "Found default configured project for {0}: {1} (in {2})",
                        fileName,
                        result._project.Value().ConfigFileName(),
                        elapsed);
                }
                else
                {
                    logger.Logf("No default configured project found for {0} (searched in {1})", fileName, elapsed);
                }
            }

            return result;
        }

        return new SearchResult();
    }

    private Dirty.SyncMapEntry<ConfiguredProjectID, Project>? FindOrCreateProject(
        string configFileName,
        Path configFilePath,
        ProjectLoadKind loadKind,
        LogTree? logger)
    {
        if (loadKind == ProjectLoadKindFind)
        {
            return _configuredProjects.Load(new ConfiguredProjectID(configFilePath)).Entry;
        }

        return _configuredProjects.LoadOrStore(
            new ConfiguredProjectID(configFilePath),
            Project.NewConfiguredProject(configFileName, configFilePath, this, logger)).Entry;
    }

    private bool UpdateInferredProjectRoots(string[] rootFileNames, LogTree? logger)
    {
        rootFileNames = Filter(rootFileNames, IsSupportedInInferredProject);
        ProjectReference[]? projectReferences = null;
        AstDiag[]? configFileParsingDiagnostics = null;
        var project = _inferredProject.Value();
        if (project is not null)
        {
            projectReferences = project.CommandLine!.ProjectReferences();
            configFileParsingDiagnostics = project.CommandLine!.Errors;
        }

        return UpdateInferredProject(
            rootFileNames,
            _compilerOptionsForInferredProjects,
            projectReferences,
            configFileParsingDiagnostics,
            _inferredContentMappers,
            logger);
    }

    private Dirty.SyncMapEntry<SyntheticProjectID, Project> UpdateOrCreateSyntheticProject(
        SyntheticProjectID projectID,
        string[] rootFileNames,
        CompilerOptions? compilerOptions,
        ProjectReference[]? projectReferences,
        AstDiag[]? configFileParsingDiagnostics,
        ModuleResolverFactory? moduleResolverFactory,
        ulong moduleResolverID,
        ContentMapper.Mapper[] contentMappers,
        LogTree? logger)
    {
        var (project, loaded) = _syntheticProjects.Load(projectID);
        if (!loaded)
        {
            var syntheticProject = Project.NewSyntheticProject(
                projectID,
                _sessionOptions.CurrentDirectory,
                compilerOptions!, // Go 允许 nil（见文件头第 7 条）
                rootFileNames,
                projectReferences!, // Go 允许 nil
                contentMappers,
                this,
                logger);
            syntheticProject.CommandLine!.Errors = configFileParsingDiagnostics!;
            syntheticProject._moduleResolverFactory = moduleResolverFactory;
            syntheticProject._moduleResolverID = moduleResolverID;
            return _syntheticProjects.LoadOrStore(projectID, syntheticProject).Entry!;
        }

        var currentProject = project!.Value();
        if (compilerOptions is null)
        {
            compilerOptions = currentProject.CommandLine!.CompilerOptions();
        }

        var newCommandLine = Project.NewInferredProjectCommandLine(
            compilerOptions!,
            rootFileNames,
            projectReferences!,
            contentMappers,
            new TSPath.ComparePathsOptions(
                useCaseSensitiveFileNames: _fs._fs.UseCaseSensitiveFileNames(),
                currentDirectory: currentProject._currentDirectory));
        newCommandLine.Errors = configFileParsingDiagnostics!;
        project.ChangeIf(
            p => !SequenceEqual(p.CommandLine!.FileNames(), newCommandLine.FileNames())
                || !CompilerOptionsDeepEqual(p.CommandLine!.CompilerOptions(), compilerOptions)
                || !ProjectReferencesEqual(p.CommandLine!.ProjectReferences(), projectReferences)
                || !DiagnosticsDeepEqual(p.CommandLine!.Errors, configFileParsingDiagnostics)
                || !SequenceEqual(p.CommandLine!.ContentMappers(), newCommandLine.ContentMappers())
                || p._moduleResolverID != moduleResolverID,
            p =>
            {
                logger?.Logf("Updating synthetic project config with {0} root files", rootFileNames.Length);
                p.SetCommandLine(newCommandLine);
                p._moduleResolverFactory = moduleResolverFactory;
                p._moduleResolverID = moduleResolverID;
            });
        return project;
    }

    /// <summary>updateInferredProject preserves the current command line when roots/options are unchanged。</summary>
    internal bool UpdateInferredProject(
        string[] rootFileNames,
        CompilerOptions? compilerOptions,
        ProjectReference[]? projectReferences,
        AstDiag[]? configFileParsingDiagnostics,
        ContentMapper.Mapper[] contentMappers,
        LogTree? logger)
    {
        if (rootFileNames.Length == 0)
        {
            return DeleteInferredProject(logger);
        }

        rootFileNames = [.. rootFileNames];
        Array.Sort(rootFileNames, StringComparer.Ordinal);
        return UpdateOrCreateInferredProject(rootFileNames, compilerOptions, projectReferences, configFileParsingDiagnostics, contentMappers, logger);
    }

    private bool DeleteInferredProject(LogTree? logger)
    {
        var project = _inferredProject.Value();
        if (project is null)
        {
            return false;
        }

        logger?.Log("Deleting inferred project");
        if (project.Program is not null)
        {
            project.Program.RangeResolvedProjectReference((referencePath, _, _, _) =>
            {
                _configFileRegistryBuilder.ReleaseConfigForProject(referencePath, project.ID());
                return true;
            });
        }

        _inferredProject.Delete();
        return true;
    }

    /// <summary>
    /// updateOrCreateInferredProject always retains an inferred project, including when rootFileNames is empty.
    /// The caller transfers ownership of rootFileNames.
    /// </summary>
    private bool UpdateOrCreateInferredProject(
        string[] rootFileNames,
        CompilerOptions? compilerOptions,
        ProjectReference[]? projectReferences,
        AstDiag[]? configFileParsingDiagnostics,
        ContentMapper.Mapper[] contentMappers,
        LogTree? logger)
    {
        var project = _inferredProject.Value();
        if (project is null)
        {
            project = Project.NewInferredProject(
                _sessionOptions.CurrentDirectory,
                compilerOptions,
                rootFileNames,
                projectReferences!, // Go 允许 nil
                contentMappers,
                this,
                logger);
            project.CommandLine!.Errors = configFileParsingDiagnostics!;
            _inferredProject.Set(project);
            return true;
        }

        if (compilerOptions is null)
        {
            compilerOptions = project.CommandLine!.CompilerOptions();
        }

        var newCommandLine = Project.NewInferredProjectCommandLine(
            compilerOptions!,
            rootFileNames,
            projectReferences!,
            contentMappers,
            new TSPath.ComparePathsOptions(
                useCaseSensitiveFileNames: _fs._fs.UseCaseSensitiveFileNames(),
                currentDirectory: project._currentDirectory));
        newCommandLine.Errors = configFileParsingDiagnostics!;
        var changed = _inferredProject.ChangeIf(
            p => !SequenceEqual(p.CommandLine!.FileNames(), newCommandLine.FileNames())
                || !CompilerOptionsDeepEqual(p.CommandLine!.CompilerOptions(), compilerOptions)
                || !ProjectReferencesEqual(p.CommandLine!.ProjectReferences(), projectReferences)
                || !DiagnosticsDeepEqual(p.CommandLine!.Errors, configFileParsingDiagnostics)
                || !SequenceEqual(p.CommandLine!.ContentMappers(), newCommandLine.ContentMappers()),
            p =>
            {
                logger?.Logf("Updating inferred project config with {0} root files", rootFileNames.Length);
                p.SetCommandLine(newCommandLine);
            });
        if (!changed)
        {
            return false;
        }

        return true;
    }

    /// <summary>Go: <c>func projectReferencesEqual(a []*core.ProjectReference, b []*core.ProjectReference) bool</c>（见文件头第 1 条）。</summary>
    internal static bool ProjectReferencesEqual(ProjectReference[]? a, ProjectReference[]? b)
    {
        var lenA = a?.Length ?? 0;
        var lenB = b?.Length ?? 0;
        if (lenA != lenB)
        {
            return false;
        }

        for (var i = 0; i < lenA; i++)
        {
            var x = a![i];
            var y = b![i];
            if (x is null || y is null)
            {
                if (!ReferenceEquals(x, y))
                {
                    return false;
                }

                continue;
            }

            if (x.Path != y.Path || x.Circular != y.Circular)
            {
                return false;
            }
        }

        return true;
    }

    private bool IsSupportedInInferredProject(string fileName)
    {
        if (IsDynamicFileName(fileName) || GetScriptKindFromFileName(fileName) != ScriptKind.Unknown)
        {
            return true;
        }

        var file = _fs.GetFile(fileName);
        if (file is not null && file.IsOverlay() && GetAnyExtensionFromPath(fileName, [], ignoreCase: false).Length == 0)
        {
            return true;
        }

        return FileExtensionIsOneOf(fileName, _inferredContentMapperExtensions);
    }

    /// <summary>
    /// updateProgram updates the program for the given project entry if necessary. It returns
    /// a boolean indicating whether the update could have caused any structure-affecting changes.
    /// </summary>
    internal bool UpdateProgram(Dirty.Value<Project> entry, LogTree? logger)
    {
        var updateProgram = false;
        var deleteProject = false;
        var filesChanged = false;
        var projectID = entry.Value().ID();
        var startTime = Stopwatch.GetTimestamp();
        var notifiedLoading = false;
        var displayName = "";
        entry.Locked(e =>
        {
            if (e.Value().Kind == KindConfigured)
            {
                var commandLine = _configFileRegistryBuilder.AcquireConfigForProject(
                    e.Value().ConfigFileName(),
                    e.Value()._configFilePath,
                    e.Value(),
                    logger?.Fork("Acquiring config for project"));
                if (commandLine is null)
                {
                    deleteProject = true;
                    filesChanged = true;
                    return;
                }

                if (!ReferenceEquals(e.Value().CommandLine, commandLine))
                {
                    updateProgram = true;
                    e.Change(p => p.SetCommandLine(commandLine));
                }
            }

            if (!updateProgram)
            {
                updateProgram = e.Value()._dirty;
            }

            if (updateProgram && _client is not null)
            {
                displayName = e.Value().DisplayName(_sessionOptions.CurrentDirectory);
                notifiedLoading = true;
            }
        });
        if (notifiedLoading && _client is not null)
        {
            _client.ProgressStart(TypeScript.Diagnostics.Diagnostics.Project_0, displayName);
        }

        if (deleteProject)
        {
            DeleteProject(entry, logger);
        }

        if (updateProgram)
        {
            entry.Locked(e =>
            {
                e.Change(project =>
                {
                    var oldHost = project._host;
                    var oldProgram = project.Program;
                    var oldCheckerPool = project._checkerPool;
                    project._host = CompilerHost.NewCompilerHost(project._currentDirectory, project, this, logger?.Fork("CompilerHost"));
                    var result = project.CreateProgram();
                    var watchedFiles = new List<string>();
                    foreach (var mapper in project.CommandLine!.ContentMappers() ?? [])
                    {
                        if (mapper.Package.Length != 0 && mapper.ContributionID.Length == 0 && mapper.PackageDirectory.Length != 0)
                        {
                            watchedFiles.Add(CombinePaths(mapper.PackageDirectory, "package.json"));
                        }
                    }

                    if (Array.Exists(result.Program!.SourceFiles(), static file => file.ContentMapper().Length != 0))
                    {
                        var contentMapperProject = project._host.ContentMapperProject();
                        if (contentMapperProject is not null)
                        {
                            watchedFiles.AddRange(contentMapperProject.WatchedFiles());
                        }
                    }

                    var sortedWatchedFiles = Watch.SortedAndCompacted(watchedFiles.ToArray());
                    project._contentMapperWatch = project._contentMapperWatch?.Clone(sortedWatchedFiles);
                    project._contentMapperWatchedFiles = NewSetWithSizeHint<Path>(sortedWatchedFiles.Length);
                    foreach (var fileName in sortedWatchedFiles)
                    {
                        project._contentMapperWatchedFiles.Add(_toPath(fileName));
                    }

                    project.Program = result.Program;
                    project._checkerPool = (CheckerPool)result.Program!.GetCheckerPool();
                    project.ProgramUpdateKind = result.UpdateKind;
                    project.ProgramLastUpdate = _newSnapshotID;
                    if (result.UpdateKind == ProgramUpdateKindCloned)
                    {
                        project._host._sourceFS._seenFiles = oldHost!._sourceFS._seenFiles;
                    }

                    if (result.UpdateKind == ProgramUpdateKindNewFiles)
                    {
                        filesChanged = true;
                        project._programFilesWatch = project.CloneWatchers();
                    }

                    project._dirty = false;
                    project._dirtyFilePath = default;
                    ReleaseDroppedProjectReferences(oldProgram, result.Program, project.ID());
                    oldCheckerPool?.Discard();
                });
            });
        }

        if (notifiedLoading && _client is not null)
        {
            _client.ProgressFinish(TypeScript.Diagnostics.Diagnostics.Project_0, displayName);
        }

        if (updateProgram && logger is not null)
        {
            logger.Logf("Program update for {0} completed in {1}", projectID, Stopwatch.GetElapsedTime(startTime));
        }

        return filesChanged;
    }

    private void MarkFilesChanged(Dirty.Value<Project> entry, Path[] paths, FileChangeType changeType, LogTree? logger)
    {
        var dirty = false;
        var dirtyFilePath = default(Path);
        entry.ChangeIf(
            p =>
            {
                if (p.Program is null || p._dirty && p._dirtyFilePath == default)
                {
                    return false;
                }

                dirtyFilePath = p._dirtyFilePath;
                foreach (var path in paths)
                {
                    if (p.ContainsFile(path))
                    {
                        dirty = true;
                        if (changeType == FileChangeType.Deleted)
                        {
                            dirtyFilePath = default;
                            break;
                        }

                        // package.json changes can affect module resolution and package
                        // identity (e.g. dedup decisions), so they must always trigger
                        // a full rebuild rather than a single-file clone.
                        if (GetBaseFileName(path.Value) == "package.json")
                        {
                            dirtyFilePath = default;
                            break;
                        }

                        if (dirtyFilePath == default)
                        {
                            dirtyFilePath = path;
                        }
                        else if (dirtyFilePath != path)
                        {
                            dirtyFilePath = default;
                            break;
                        }
                    }
                    else if (p._host is not null
                        && (changeType == FileChangeType.Created && p._host._sourceFS.SeenFileOrMissingParentDirectory(path)
                            || changeType != FileChangeType.Created && p._host._sourceFS.SeenFile(path)))
                    {
                        dirty = true;
                        dirtyFilePath = default;
                        break;
                    }
                }

                return dirty || p._dirtyFilePath != dirtyFilePath;
            },
            p =>
            {
                p._dirty = true;
                p._dirtyFilePath = dirtyFilePath;
                if (logger is not null)
                {
                    if (dirtyFilePath != default)
                    {
                        logger.Logf("Marking project {0} as dirty due to changes in {1}", p.ID().Value, dirtyFilePath.Value);
                    }
                    else
                    {
                        logger.Logf("Marking project {0} as dirty", p.ID().Value);
                    }
                }
            });
    }

    private void DeleteProject(Dirty.Value<Project> project, LogTree? logger)
    {
        var value = project.Value();
        var projectID = value.ID();
        logger?.Logf("Deleting {0} project: {1}", value.Kind.String(), value.ID().Value);
        if (value.Program is not null)
        {
            value.Program.RangeResolvedProjectReference((referencePath, _, _, _) =>
            {
                _configFileRegistryBuilder.ReleaseConfigForProject(referencePath, projectID);
                return true;
            });
        }

        if (value.Kind == KindConfigured)
        {
            _configFileRegistryBuilder.ReleaseConfigForProject(value.ConfigFilePath(), projectID);
        }

        project.Delete();
    }

    /// <summary>
    /// releaseDroppedProjectReferences releases the config entries for project references
    /// that were present in oldProgram but are no longer referenced by newProgram. Creating
    /// newProgram already re-acquires the config for every reference it still resolves, so
    /// only the dropped references need to be released here.
    /// </summary>
    private void ReleaseDroppedProjectReferences(Program? oldProgram, Program? newProgram, ID projectID)
    {
        if (oldProgram is null || ReferenceEquals(oldProgram, newProgram))
        {
            return;
        }

        var newReferences = new Collections.Set<Path>();
        if (newProgram is not null)
        {
            newProgram.RangeResolvedProjectReference((referencePath, _, _, _) =>
            {
                newReferences.Add(referencePath);
                return true;
            });
        }

        oldProgram.RangeResolvedProjectReference((referencePath, _, _, _) =>
        {
            if (!newReferences.Has(referencePath))
            {
                _configFileRegistryBuilder.ReleaseConfigForProject(referencePath, projectID);
            }

            return true;
        });
    }

    /// <summary>
    /// Go: <c>reflect.DeepEqual(a, b)</c> 对 <c>*core.CompilerOptions</c>（禁反射 ⇒ 复用字段表，
    /// 见文件头第 8 条 / CompilerOptionFields.cs 文件头）。
    /// </summary>
    private static bool CompilerOptionsDeepEqual(CompilerOptions? a, CompilerOptions? b)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }

        foreach (var field in TsOptions.TsOptions.CompilerOptionFields)
        {
            if (field.Changed(a, b))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Go: <c>reflect.DeepEqual(a, b)</c> 对 <c>[]*ast.Diagnostic</c>（见文件头第 8 条）。</summary>
    private static bool DiagnosticsDeepEqual(AstDiag[]? a, AstDiag[]? b)
    {
        var lenA = a?.Length ?? 0;
        var lenB = b?.Length ?? 0;
        if (lenA != lenB)
        {
            return false;
        }

        for (var i = 0; i < lenA; i++)
        {
            if (!TypeScript.Ast.Ast.EqualDiagnostics(a![i], b![i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Go: <c>maps.Equal(a, b)</c>（见文件头第 14 条）。</summary>
    private static bool MapsEqual<K, V>(Dictionary<K, V>? a, Dictionary<K, V>? b) where K : notnull
    {
        var lenA = a?.Count ?? 0;
        var lenB = b?.Count ?? 0;
        if (lenA != lenB)
        {
            return false;
        }

        if (a is null)
        {
            return true;
        }

        foreach (var (key, value) in a)
        {
            if (b is null || !b.TryGetValue(key, out var other) || !EqualityComparer<V>.Default.Equals(value, other))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Go: <c>slices.Delete(xs, slices.Index(xs, item), slices.Index(xs, item)+1)</c>
    /// （删除第一处出现，见文件头第 10 条）。
    /// </summary>
    private static string[] DeleteFirst(string[] array, string item)
    {
        var index = Array.IndexOf(array, item);
        if (index < 0)
        {
            return array;
        }

        var result = new string[array.Length - 1];
        Array.Copy(array, 0, result, 0, index);
        Array.Copy(array, index + 1, result, index, array.Length - index - 1);
        return result;
    }

    /// <summary>Go 的 <c>%v</c> 打印集合（Go 的顺序即 map 迭代顺序，同样不确定）。</summary>
    private static string FormatSet(Collections.Set<ID> set)
    {
        var items = new List<string>(set.Len());
        foreach (var id in set.M)
        {
            items.Add(id.Value);
        }

        return "[" + string.Join(" ", items) + "]";
    }

    /// <summary>Go: <c>slices.Equal(a, b)</c>（顺序敏感，见文件头第 9 条）。</summary>
    private static bool SequenceEqual<T>(T[]? a, T[]? b)
    {
        var lenA = a?.Length ?? 0;
        var lenB = b?.Length ?? 0;
        if (lenA != lenB)
        {
            return false;
        }

        for (var i = 0; i < lenA; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(a![i], b![i]))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>Go 的未导出 <c>type searchNode struct</c>（projectcollectionbuilder.go:1042-1046）。</summary>
internal sealed class SearchNode
{
    internal string _configFileName = "";
    internal ProjectLoadKind _loadKind;
    internal LogTree? _logger;
}

/// <summary>Go 的未导出 <c>type searchNodeKey struct</c>（projectcollectionbuilder.go:1048-1051；值语义 = Go 的 `==`）。</summary>
internal readonly record struct SearchNodeKey(string ConfigFileName, ProjectLoadKind LoadKind);

/// <summary>Go 的未导出 <c>type searchResult struct</c>（projectcollectionbuilder.go:1053-1056）。</summary>
internal sealed class SearchResult
{
    internal Dirty.SyncMapEntry<ConfiguredProjectID, Project>? _project;
    internal Collections.Set<Path> _retain = new();
}
