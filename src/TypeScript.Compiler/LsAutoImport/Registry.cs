// ported from: internal/ls/autoimport/registry.go（registry.go:1-1230；`bucketBuildResult` 之后的构建逻辑在
// Registry.Build.cs，拆分点 = Go 的 `buildProjectBucket` 之前）。
//
// 与 Go 的偏离（逐条）：
// 1) `sync.WaitGroup` + `wg.Go(...)`（Go 1.25）→ `Core.WorkGroup`（`NewWorkGroup(false)`，见
//    Core/Workgroup.cs）。**WorkGroup 不能在 RunAndWait 之后复用**（会 Debug.Fail），所以 Go 里复用同一个
//    wg 的多个阶段各自新建一个 WorkGroup：按 Go 的每个 `wg.Wait()` 切分，语义相同（每阶段都等全部完成）。
// 2) Go 的 `wg.Go` 闭包**按迭代**捕获循环变量（Go 1.22+）→ C# 的 `foreach` 变量天然每轮一个；只有
//    `for (var i = 0; …)` 这种共享循环变量需要在排队前拷一份（见 Registry.Build.cs 的 `ExtractPackage`）。
// 3) `sync.Mutex` → `lock (_mu)`（普通 object）。
// 4) `ctx.Err() != nil` → `ctx.CancellationToken.IsCancellationRequested`（Core/Context.cs 的对应物，仓内
//    既有约定，见 Program.Emit.cs 文件头）。
// 5) Go 的包级函数 → 静态类 `AutoImport` 上的 `internal static`/`public static` 成员（名字 PascalCase）：
//    `NewRegistry`/`NewRegistryBucket`/`NewRegistryBuilder`/`InstallExtractions`/`RecursiveSearchSubset`/
//    `HasNewNonNodeModulesFiles`/`IsIgnoredFile`/`HasSymlinkToNodeModules`/
//    `BucketBuildPreferencesFromUserPreferences`。未导出类型 → C# 习惯命名：`registryBuilder`→`RegistryBuilder`、
//    `directory`→`RegistryDirectory`（**不能**叫 `Directory`，会撞 `System.IO.Directory`）、
//    `failedAmbientModuleLookupSource`→`FailedAmbientModuleLookupSource`；未导出字段 → `_camelCase`。
// 6) **Go 的 nil 接收者安全方法在 C# 调用点一律写 `?.`**（§3.2）：
//    - `logging.LogTree.Fork`（Go 里 `if c == nil { return nil }`）→ `logger?.Fork(...)`；
//      `Logf` 保持 Go 的 `if logger != nil` 守护；
//    - `packagejson.InfoCacheEntry.Exists()`（Go 里 `p != nil && p.Contents != nil`）→ `dir._packageJson?.Exists() ?? false`；
//    - `collections.Set` 的方法（Go 里 nil 接收者返回零值）→ `set?.Len() ?? 0` 等。
// 7) `time.Since(start)` → `Stopwatch.GetElapsedTime(start)`（仓内约定，见 Ipc/ConnSync.cs）；日志格式串改成
//    C# 复合格式（`{0}`），`TimeSpan` 直接以 `{0}` 打印（Go 的 `%v` 打印 Go 自己的 duration 文本，两者文本不同）。
// 8) `module.ResolverOptions` 在 Go 里是**值类型**（`resolverOptions := b.resolverOptions` 是值拷贝），C# 的
//    `ResolverOptions` 是 class → 每处都必须新建实例（`ResolverOptionsCopy()`），否则 `GetModuleResolver`/
//    `NewResolver` 的字段赋值会互相污染（Util.cs 的 `GetModuleResolver` 会改写传入的 opts）。
// 9) `Dictionary`/`Set` 的 Go nil 语义（读为零值、range 为空）在 C# 里用 `?.`/`is { } x`/`?? []` 表达：
//    Registry 的四个集合字段一律初始化为空集合（Go 里 `NewRegistry` 只初始化 `directories`，其余是 nil map）。
// 10) `core.FirstResult(x.Finalize())` / `core.FirstResult(tuple)`（Go 取多返回值第一个）→ C# 直接元组解构。
// 11) `strings.Index(string(path), "/node_modules/")` → `((string)path).IndexOf("/node_modules/", StringComparison.Ordinal)`；
//     子串切片 `path[:idx]` → `((string)path)[..idx]`。
// 12) `BucketState` 是 Go 的**值类型**（`bucket.state.dirtyFile = f` 这种写入必须落在宿主上）→ C# 用 `struct` +
//     可直接写的字段（`bucket._state._dirtyFile = f`）；`Clone()` 返回新结构体。
// 13) `Set.Equals` 在 Go 里 nil 接收者安全（`if s == nil || other == nil { return false }`），C# 的实例方法在
//     null 上不可分派 → 用本文件 C#-only 的 `RegistryUtil.SetsEqual`（`a is null ? b is null : a.Equals(b)`）。
// 14) Go 的 `tspath.Path` 零值只有一种（`""`），而 C# 的 `record struct Path` 的 `default` 把 `Value` 留成
//     null（与 `(Path)""` 不相等）→ 所有"Path 是否为空"的判定走 C#-only 的 `RegistryUtil.IsEmptyPath`；
//     **两个真实 Path 之间**的比较（都来自 `toPath(...)`）直接用 `==`/`!=`。
// 15) `failedAmbientModuleLookupSource.mu sync.Mutex` 在 Go 里**从未被使用**（死字段）→ C# 侧不移植。
// 16) `TypeScript.Checker`/`TypeScript.Module`/`TypeScript.Compiler`/`TypeScript.LsUtil` 这些**命名空间**与其中
//     的同名类型/静态类同名（§2 的坑）：类型一律全限定（`TypeScript.Checker.Checker`、
//     `TypeScript.Compiler.Program`、`TypeScript.LsUtil.UserPreferences`、`TypeScript.Module.DefaultResolver`），
//     包级函数用 `using static`（`NewResolver`、`FileNameToDocumentURI`…）。
// 17) Go 的 `entry.Change(func(bucket *RegistryBucket) {...})` 里就地 `delete(cleanNodeModulesBuckets, k)`/`delete(cleanProjectBuckets, k)`
//     （Go 允许边 range 边删）→ C# 的 HashSet 不允许遍历中修改：改成先收集、内层循环结束后统一删除
//     （被删项已置 `multipleFilesDirty`，重复标记是幂等的，行为无差）。
// 18) 日志里把 ProjectID 以 `%s` 打印的地方（Go 走 `fmt.Stringer`）→ 显式 `.String()`。
using System.Threading;
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.LsProto;
using TypeScript.LsUtil;
using TypeScript.Module;
using TypeScript.PackageJson;
using TypeScript.Project.Logging;
using TypeScript.Symlinks;
using TypeScript.Vfs;
// `Map`/`MapEntry`/`MapBuilder`/`Cloneable`（TypeScript.Project.Dirty）与 `Set`/`SyncMap`/`SyncSet`
// （TypeScript.Collections）同时可见时 `SyncMap`/`MapEntry` 会 CS0104：这里用命名空间别名区分，
// 只给 Dirty 的类型加 `Dirty.` 前缀（与 Project/ConfigFileRegistryBuilder.cs 的 `Dirty.SyncMap` 同一手法）。
using Dirty = TypeScript.Project.Dirty;
using Path = TypeScript.TSPath.Path;
using Stopwatch = System.Diagnostics.Stopwatch;
using static TypeScript.Collections.Collections;
using static TypeScript.Core.Core;
using static TypeScript.LsAutoImport.AutoImport;
using static TypeScript.LsConv.LsConv;
using static TypeScript.Module.Module;
using static TypeScript.Project.Dirty.Dirty;
using static TypeScript.TSPath.TSPath;

namespace TypeScript.LsAutoImport;

/// <summary>C#-only 小工具（Go 里不需要，见文件头偏离 13/14）：`Path`/`Set` 的"Go 零值"语义补齐。</summary>
internal static class RegistryUtil
{
    /// <summary>Go 的 <c>p == ""</c>（`tspath.Path` 的零值只有一种；C# 的 <c>default(Path).Value</c> 是 null）。</summary>
    internal static bool IsEmptyPath(Path path) => path.Value is null or "";

    /// <summary>Go 的 <c>a.Equals(b)</c>（`*collections.Set` 的 nil 接收者安全版本：<c>a == nil || b == nil</c> ⇒ false）。</summary>
    internal static bool SetsEqual(Set<string>? a, Set<string>? b) => a is null ? b is null : a.Equals(b);
}

/// <summary>Go: <c>type ProjectID interface { fmt.Stringer }</c>（registry.go:34-36）。</summary>
public interface ProjectID
{
    /// <summary>Go 的 <c>fmt.Stringer.String()</c>（C# 里照 Go 保留方法名）。</summary>
    string String();
}

/// <summary>Go: <c>type newProgramStructure int</c>（registry.go:87-93）。成员名去类型前缀，数值与 Go 一致。</summary>
internal enum NewProgramStructure
{
    False = 0,
    SameFileNames = 1,
    DifferentFileNames = 2,
}

/// <summary>Go: <c>type bucketBuildPreferences struct</c>（registry.go:95-142）。</summary>
internal struct BucketBuildPreferences
{
    internal string[]? _fileExcludePatterns;

    internal Tristate _autoImportEntrypointDirectorySearch;

    internal bool Equal(BucketBuildPreferences other) =>
        UnorderedEqual(_fileExcludePatterns, other._fileExcludePatterns) &&
        _autoImportEntrypointDirectorySearch == other._autoImportEntrypointDirectorySearch;

    internal BucketBuildPreferences Clone()
    {
        var result = this;
        result._fileExcludePatterns = _fileExcludePatterns is null ? null : (string[])_fileExcludePatterns.Clone();
        return result;
    }
}

/// <summary>Go: <c>type BucketState struct</c>（registry.go:144-230）：桶的脏状态。</summary>
public struct BucketState
{
    internal Path _dirtyFile;

    internal bool _multipleFilesDirty;

    internal NewProgramStructure _newProgramStructure;

    internal BucketBuildPreferences _buildPreferences;

    internal Set<string>? _dirtyPackages;

    internal Set<string>? _recursiveSearchPackages;

    public BucketState Clone()
    {
        var result = this;
        result._buildPreferences = _buildPreferences.Clone();
        result._dirtyPackages = _dirtyPackages?.Clone();
        result._recursiveSearchPackages = _recursiveSearchPackages?.Clone();
        return result;
    }

    public bool Dirty() =>
        _multipleFilesDirty ||
        !RegistryUtil.IsEmptyPath(_dirtyFile) ||
        (int)_newProgramStructure > 0 ||
        (_dirtyPackages?.Len() ?? 0) > 0;

    public Path DirtyFile() => _multipleFilesDirty ? default : _dirtyFile;

    public Set<string>? DirtyPackages() => _multipleFilesDirty ? null : _dirtyPackages;

    public Set<string>? RecursiveSearchPackages() => _recursiveSearchPackages;

    internal bool PossiblyNeedsRebuildForFile(Path file, UserPreferences preferences) =>
        (int)_newProgramStructure > 0 ||
        HasDirtyFileBesides(file) ||
        !_buildPreferences.Equal(BucketBuildPreferencesFromUserPreferences(preferences)) ||
        (_dirtyPackages?.Len() ?? 0) > 0;

    internal bool HasDirtyFileBesides(Path file) =>
        _multipleFilesDirty || (!RegistryUtil.IsEmptyPath(_dirtyFile) && _dirtyFile != file);
}

/// <summary>Go: <c>type RegistryBucket struct</c>（registry.go:232-286）。</summary>
public sealed class RegistryBucket : Dirty.Cloneable<RegistryBucket>
{
    internal BucketState _state;

    /// <summary>Paths maps file paths to package names（项目桶恒为空串；node_modules 桶用于粒度更新的反查）。
    /// bucket 定稿后视为不可变，只能整体替换。</summary>
    public Dictionary<Path, string>? Paths;

    /// <summary>PackageFiles maps package names to their file paths and file names（只对 node_modules 桶定义）。</summary>
    public Dictionary<string, Dictionary<Path, string>?>? PackageFiles;

    /// <summary>只对项目桶定义。</summary>
    public Set<string>? ResolvedPackageNames;

    /// <summary>只对 node_modules 桶定义；nil = 不做 package.json 过滤。</summary>
    public Set<string>? DependencyNames;

    /// <summary>只对 node_modules 桶定义。</summary>
    public Dictionary<string, string[]>? AmbientModuleNames;

    /// <summary>定稿后视为不可变：要改先 Clone 再整体替换。</summary>
    public Index<Export>? Index;

    public RegistryBucket Clone() => new()
    {
        _state = _state.Clone(),
        Paths = Paths,
        PackageFiles = PackageFiles,
        ResolvedPackageNames = ResolvedPackageNames,
        DependencyNames = DependencyNames,
        AmbientModuleNames = AmbientModuleNames,
        Index = Index,
    };

    /// <summary>Go: <c>func (b *RegistryBucket) markProjectFileDirty(file tspath.Path)</c>（registry.go:270-277）。</summary>
    internal void MarkProjectFileDirty(Path file)
    {
        if (_state.HasDirtyFileBesides(file))
        {
            _state._multipleFilesDirty = true;
        }
        else
        {
            _state._dirtyFile = file;
        }
    }

    /// <summary>Go: <c>func (b *RegistryBucket) markNodeModulesDirty(packageName string)</c>（registry.go:279-286）。</summary>
    internal void MarkNodeModulesDirty(string packageName)
    {
        if (_state._multipleFilesDirty)
        {
            return;
        }

        if (packageName == "")
        {
            _state._multipleFilesDirty = true;
            return;
        }

        // Track the package for granular updates
        _state._dirtyPackages ??= new Set<string>();
        _state._dirtyPackages.Add(packageName);
    }
}

/// <summary>Go 的未导出类型 <c>directory</c>（registry.go:288-297）。名字不能叫 `Directory`（撞 `System.IO.Directory`）。</summary>
internal sealed class RegistryDirectory : Dirty.Cloneable<RegistryDirectory>
{
    internal string _name = "";

    internal InfoCacheEntry? _packageJson;

    internal bool _hasNodeModules;

    public RegistryDirectory Clone() => new()
    {
        _name = _name,
        _packageJson = _packageJson,
        _hasNodeModules = _hasNodeModules,
    };
}

/// <summary>Go: <c>type Registry struct</c>（registry.go:299-344）。</summary>
public sealed class Registry
{
    internal Func<string, Path> _toPath = null!;

    internal UserPreferences _userPreferences;

    internal Dictionary<Path, RegistryDirectory> _directories = [];

    internal Dictionary<Path, RegistryBucket> _nodeModules = [];

    internal Dictionary<ProjectID, RegistryBucket> _projects = [];

    internal int _uniquePackageCount;

    /// <summary>entrypoints maps from file path to the resolved entrypoints for that file（跨所有 node_modules 桶共享）。</summary>
    internal Dictionary<Path, ResolvedEntrypoint[]> _entrypoints = [];

    /// <summary>specifierCache maps from importing file to target file to specifier。</summary>
    internal Dictionary<Path, SyncMap<Path, string>> _specifierCache = [];

    /// <summary>Go: <c>func (r *Registry) IsPreparedForImportingFile(...)</c>（registry.go:355-381）。
    /// Go 的 `if r == nil { return false }` 分支在 C# 里不可达（实例方法不能在 null 上分派），调用方自行判空。</summary>
    public bool IsPreparedForImportingFile(string fileName, ProjectID projectID, UserPreferences preferences)
    {
        if (!_projects.TryGetValue(projectID, out var projectBucket))
        {
            return false;
        }

        var path = _toPath(fileName);
        if (projectBucket._state.PossiblyNeedsRebuildForFile(path, preferences))
        {
            return false;
        }

        var dirPath = path.GetDirectoryPath();
        while (true)
        {
            if (_nodeModules.TryGetValue(dirPath, out var dirBucket))
            {
                if (dirBucket._state.PossiblyNeedsRebuildForFile(path, preferences))
                {
                    return false;
                }
            }

            var parent = dirPath.GetDirectoryPath();
            if (parent == dirPath)
            {
                break;
            }

            dirPath = parent;
        }

        return true;
    }

    /// <summary>Go: <c>func (r *Registry) NodeModulesDirectories() map[tspath.Path]string</c>（registry.go:383-391）。</summary>
    public Dictionary<Path, string> NodeModulesDirectories()
    {
        var dirs = new Dictionary<Path, string>();
        foreach (var (dirPath, dir) in _directories)
        {
            if (dir._hasNodeModules)
            {
                dirs[(Path)CombinePaths((string)dirPath, "node_modules")] = CombinePaths(dir._name, "node_modules");
            }
        }

        return dirs;
    }

    /// <summary>Go: <c>func (r *Registry) Clone(ctx, change, host, logger) (*Registry, error)</c>（registry.go:393-414）。
    /// Go 恒返回 nil error（`bucketBuildResult.err` 只在内部用于跳过桶替换），故按 §3.2 落成"返回值 + 抛异常"。</summary>
    public Registry Clone(Context ctx, RegistryChange change, RegistryCloneHost host, LogTree? logger)
    {
        var start = Stopwatch.GetTimestamp();
        if (logger is not null)
        {
            logger = logger.Fork("Building autoimport registry");
        }

        var builder = NewRegistryBuilder(this, host);
        if (change.UserPreferences is not null)
        {
            builder._userPreferences = change.UserPreferences.Value;
            if (!UnorderedEqual(builder._userPreferences.AutoImportSpecifierExcludeRegexes, _userPreferences.AutoImportSpecifierExcludeRegexes))
            {
                builder._specifierCache.Clear();
            }
        }

        builder.UpdateBucketAndDirectoryExistence(change, logger);
        builder.MarkBucketsDirty(change, logger);
        if (!RegistryUtil.IsEmptyPath(change.RequestedFile))
        {
            builder.UpdateIndexes(ctx, change, logger);
        }

        if (logger is not null)
        {
            logger.Logf("Built autoimport registry in {0}", Stopwatch.GetElapsedTime(start));
        }

        return builder.Build();
    }

    /// <summary>Go: <c>func (r *Registry) GetCacheStats() *CacheStats</c>（registry.go:428-482）。</summary>
    public CacheStats GetCacheStats()
    {
        var stats = new CacheStats { UniquePackageCount = _uniquePackageCount };

        foreach (var (projectID, bucket) in _projects)
        {
            stats.ProjectBuckets.Add(new BucketStats
            {
                Name = projectID.String(),
                ExportCount = bucket.Index is null ? 0 : bucket.Index._entries.Count,
                FileCount = bucket.Paths?.Count ?? 0,
                State = bucket._state,
                DependencyNames = bucket.DependencyNames,
                PackageNames = null,
            });
        }

        foreach (var (path, bucket) in _nodeModules)
        {
            // Derive PackageNames from PackageFiles keys
            Set<string>? packageNames = null;
            var fileCount = 0;
            if (bucket.PackageFiles is not null)
            {
                packageNames = NewSetWithSizeHint<string>(bucket.PackageFiles.Count);
                foreach (var (name, paths) in bucket.PackageFiles)
                {
                    packageNames.Add(name);
                    fileCount += paths?.Count ?? 0;
                }
            }

            stats.NodeModulesBuckets.Add(new BucketStats
            {
                Name = (string)path,
                ExportCount = bucket.Index is null ? 0 : bucket.Index._entries.Count,
                FileCount = fileCount,
                State = bucket._state,
                DependencyNames = bucket.DependencyNames,
                PackageNames = packageNames,
            });
        }

        stats.ProjectBuckets.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
        stats.NodeModulesBuckets.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));

        return stats;
    }
}

/// <summary>Go: <c>type BucketStats struct</c>（registry.go:416-424）。</summary>
public sealed class BucketStats
{
    public string Name = "";

    public int ExportCount;

    public int FileCount;

    public BucketState State;

    public Set<string>? DependencyNames;

    public Set<string>? PackageNames;
}

/// <summary>Go: <c>type CacheStats struct</c>（registry.go:426-430）。</summary>
public sealed class CacheStats
{
    public List<BucketStats> ProjectBuckets = [];

    public List<BucketStats> NodeModulesBuckets = [];

    public int UniquePackageCount;
}

/// <summary>Go: <c>type RegistryChange struct</c>（registry.go:484-496）。</summary>
public sealed class RegistryChange
{
    public Path RequestedFile;

    public Dictionary<Path, string>? OpenFiles;

    public Set<DocumentUri>? Changed;

    public Set<DocumentUri>? Created;

    public Set<DocumentUri>? Deleted;

    /// <summary>RebuiltPrograms maps from project ID to: true = 文件名集合变了；false = 文件名集合没变。</summary>
    public Dictionary<ProjectID, bool>? RebuiltPrograms;

    public UserPreferences? UserPreferences;
}

/// <summary>Go: <c>type RegistryCloneHost interface</c>（registry.go:498-506）。</summary>
public interface RegistryCloneHost : TypeScript.Module.ResolutionHost
{
    /// <summary>Go 重复声明了嵌入接口里的 <c>FS()</c>（C# 用 <c>new</c> 隐藏，签名相同）。</summary>
    new FS FS();

    /// <summary>Go 返回 <c>(ProjectID, *compiler.Program)</c> 两个值（**不是** ok 布尔）；判空按 <c>Id is not null</c>。</summary>
    (ProjectID Id, TypeScript.Compiler.Program? Program) GetDefaultProject(Path path);

    TypeScript.Compiler.Program? GetProgramForProject(ProjectID projectID);

    InfoCacheEntry? GetPackageJson(string fileName);

    SourceFile? GetSourceFile(string fileName, Path path);

    void Dispose();
}

/// <summary>Go: <c>type registryBuilder struct</c>（registry.go:508-525；跨文件 partial：构建逻辑见 Registry.Build.cs）。</summary>
internal sealed partial class RegistryBuilder
{
    internal RegistryCloneHost _host = null!;

    internal Registry _base = null!;

    internal UserPreferences _userPreferences;

    internal Dirty.Map<Path, RegistryDirectory> _directories = null!;

    internal Dirty.Map<Path, RegistryBucket> _nodeModules = null!;

    internal Dirty.Map<ProjectID, RegistryBucket> _projects = null!;

    internal Dirty.MapBuilder<Path, SyncMap<Path, string>, SyncMap<Path, string>> _specifierCache = null!;

    internal ResolverOptions _resolverOptions = new();

    internal int _uniquePackageCount;

    internal Dirty.MapBuilder<Path, ResolvedEntrypoint[], ResolvedEntrypoint[]> _entrypoints = null!;

    /// <summary>Go: <c>func (b *registryBuilder) Build() *Registry</c>（registry.go:548-560）。</summary>
    internal Registry Build()
    {
        var (directories, _) = _directories.Finalize();
        var (nodeModules, _) = _nodeModules.Finalize();
        var (projects, _) = _projects.Finalize();

        return new Registry
        {
            _toPath = _base._toPath,
            _userPreferences = _userPreferences,
            _directories = directories ?? [],
            _nodeModules = nodeModules ?? [],
            _projects = projects ?? [],
            _specifierCache = _specifierCache.Build() ?? [],
            _uniquePackageCount = _uniquePackageCount,
            _entrypoints = _entrypoints.Build() ?? [],
        };
    }

    /// <summary>Go: <c>func (b *registryBuilder) updateBucketAndDirectoryExistence(change RegistryChange, logger *logging.LogTree)</c>
    /// （registry.go:562-716）。</summary>
    internal void UpdateBucketAndDirectoryExistence(RegistryChange change, LogTree? logger)
    {
        var start = Stopwatch.GetTimestamp();
        var neededProjects = new Dictionary<ProjectID, byte>();
        var neededDirectories = new Dictionary<Path, string>();
        if (change.OpenFiles is not null)
        {
            foreach (var (path, fileName) in change.OpenFiles)
            {
                var (projectID, _) = _host.GetDefaultProject(path);
                if (projectID is not null)
                {
                    neededProjects[projectID] = 0;
                }

                if (IsDynamicFileName(fileName))
                {
                    continue;
                }

                var dir = fileName;
                var dirPath = path;
                while (true)
                {
                    dir = GetDirectoryPath(dir);
                    var lastDirPath = dirPath;
                    dirPath = dirPath.GetDirectoryPath();
                    if (dirPath == lastDirPath)
                    {
                        break;
                    }

                    if (neededDirectories.ContainsKey(dirPath))
                    {
                        break;
                    }

                    neededDirectories[dirPath] = dir;
                }

                if (!_specifierCache.Has(path))
                {
                    _specifierCache.Set(path, new SyncMap<Path, string>());
                }
            }
        }

        if (!RegistryUtil.IsEmptyPath(change.RequestedFile))
        {
            var (projectID, _) = _host.GetDefaultProject(change.RequestedFile);
            if (projectID is not null)
            {
                neededProjects[projectID] = 0;
            }

            if (!_specifierCache.Has(change.RequestedFile))
            {
                _specifierCache.Set(change.RequestedFile, new SyncMap<Path, string>());
            }
        }

        foreach (var path in _base._specifierCache.Keys)
        {
            if ((change.OpenFiles is null || !change.OpenFiles.ContainsKey(path)) && path != change.RequestedFile)
            {
                _specifierCache.Delete(path);
            }
        }

        var addedProjects = new List<ProjectID>();
        var removedProjects = new List<ProjectID>();
        DiffMapsFunc(
            _base._projects,
            neededProjects,
            (_, _) =>
            {
                // Go 在这里 panic("never called because onChanged is nil")；C# 同样不会走到（onChanged 为 null）。
                Debug.Fail("never called because onChanged is nil");
                throw new System.Diagnostics.UnreachableException();
            },
            (projectID, _) =>
            {
                // Need and don't have
                _projects.Add(projectID, NewRegistryBucket());
                addedProjects.Add(projectID);
            },
            (projectID, _) =>
            {
                // Have and don't need
                _projects.Delete(projectID);
                removedProjects.Add(projectID);
            },
            null);

        if (logger is not null)
        {
            foreach (var projectID in addedProjects)
            {
                logger.Logf("Added project: {0}", projectID.String());
            }

            foreach (var projectID in removedProjects)
            {
                logger.Logf("Removed project: {0}", projectID.String());
            }
        }

        void UpdateDirectory(Path dirPath, string dirName, bool packageJsonChanged)
        {
            var packageJsonFileName = CombinePaths(dirName, "package.json");
            var hasNodeModules = _host.FS().DirectoryExists(CombinePaths(dirName, "node_modules"));
            var (entry, ok) = _directories.Get(dirPath);
            if (ok)
            {
                entry!.ChangeIf(
                    dir => packageJsonChanged || dir._hasNodeModules != hasNodeModules,
                    dir =>
                    {
                        dir._packageJson = _host.GetPackageJson(packageJsonFileName);
                        dir._hasNodeModules = hasNodeModules;
                    });
            }
            else
            {
                _directories.Add(dirPath, new RegistryDirectory
                {
                    _name = dirName,
                    _packageJson = _host.GetPackageJson(packageJsonFileName),
                    _hasNodeModules = hasNodeModules,
                });
            }

            if (hasNodeModules)
            {
                var (_, exists) = _nodeModules.Get(dirPath);
                if (!exists)
                {
                    _nodeModules.Add(dirPath, NewRegistryBucket());
                }
            }
            else
            {
                _nodeModules.TryDelete(dirPath);
            }
        }

        bool PackageJsonChanged(string dirName)
        {
            var uri = FileNameToDocumentURI(CombinePaths(dirName, "package.json"));
            return (change.Changed?.Has(uri) ?? false) ||
                   (change.Deleted?.Has(uri) ?? false) ||
                   (change.Created?.Has(uri) ?? false);
        }

        var addedNodeModulesDirs = new List<Path>();
        var removedNodeModulesDirs = new List<Path>();
        DiffMapsFunc(
            _base._directories,
            neededDirectories,
            (dir, dirName) => !PackageJsonChanged(dirName) &&
                dir._hasNodeModules == _host.FS().DirectoryExists(CombinePaths(dirName, "node_modules")),
            (dirPath, dirName) =>
            {
                // Need and don't have
                var hadNodeModules = _base._nodeModules.ContainsKey(dirPath);
                UpdateDirectory(dirPath, dirName, false);
                if (logger is not null)
                {
                    logger.Logf("Added directory: {0}", dirPath);
                }

                var (_, hasNow) = _nodeModules.Get(dirPath);
                if (hasNow && !hadNodeModules)
                {
                    addedNodeModulesDirs.Add(dirPath);
                }
            },
            (dirPath, _) =>
            {
                // Have and don't need
                var hadNodeModules = _base._nodeModules.ContainsKey(dirPath);
                _directories.Delete(dirPath);
                _nodeModules.TryDelete(dirPath);
                if (logger is not null)
                {
                    logger.Logf("Removed directory: {0}", dirPath);
                }

                if (hadNodeModules)
                {
                    removedNodeModulesDirs.Add(dirPath);
                }
            },
            (dirPath, _, dirName) =>
            {
                UpdateDirectory(dirPath, dirName, PackageJsonChanged(dirName));
                if (logger is not null)
                {
                    logger.Logf("Changed directory: {0}", dirPath);
                }
            });

        if (logger is not null)
        {
            foreach (var dirPath in addedNodeModulesDirs)
            {
                logger.Logf("Added node_modules bucket: {0}", dirPath);
            }

            foreach (var dirPath in removedNodeModulesDirs)
            {
                logger.Logf("Removed node_modules bucket: {0}", dirPath);
            }

            logger.Logf("Updated buckets and directories in {0}", Stopwatch.GetElapsedTime(start));
        }
    }

    /// <summary>Go: <c>func (b *registryBuilder) markBucketsDirty(change RegistryChange, logger *logging.LogTree)</c>
    /// （registry.go:718-810）。</summary>
    internal void MarkBucketsDirty(RegistryChange change, LogTree? logger)
    {
        // Mark new program structures
        if (change.RebuiltPrograms is not null)
        {
            foreach (var (projectID, newFileNames) in change.RebuiltPrograms)
            {
                var (bucket, ok) = _projects.Get(projectID);
                if (ok)
                {
                    bucket!.Change(b => b._state._newProgramStructure = IfElse(
                        newFileNames,
                        NewProgramStructure.DifferentFileNames,
                        NewProgramStructure.SameFileNames));
                }
            }
        }

        // Mark files dirty, bailing out if all buckets already have multiple files dirty
        var cleanNodeModulesBuckets = new HashSet<Path>();
        var cleanProjectBuckets = new HashSet<ProjectID>();
        _nodeModules.Range(entry =>
        {
            if (!entry.Value()._state._multipleFilesDirty)
            {
                cleanNodeModulesBuckets.Add(entry.Key());
            }

            return true;
        });
        _projects.Range(entry =>
        {
            if (!entry.Value()._state._multipleFilesDirty)
            {
                cleanProjectBuckets.Add(entry.Key());
            }

            return true;
        });

        void MarkFilesDirty(Set<DocumentUri>? uris)
        {
            if (cleanNodeModulesBuckets.Count == 0 && cleanProjectBuckets.Count == 0)
            {
                return;
            }

            if (uris is null)
            {
                return;
            }

            foreach (var uri in uris.Keys())
            {
                var path = _base._toPath(uri.FileName());
                if (cleanNodeModulesBuckets.Count > 0)
                {
                    // For node_modules, mark the bucket dirty if anything changes in the directory.
                    // The path could be either a symlink path (containing /node_modules/) or a realpath
                    // (for symlinked project references). Both are recorded in Paths for granular updates.
                    var nodeModulesIndex = ((string)path).IndexOf("/node_modules/", StringComparison.Ordinal);
                    if (nodeModulesIndex != -1)
                    {
                        var dirPath = (Path)((string)path)[..nodeModulesIndex];
                        if (cleanNodeModulesBuckets.Contains(dirPath))
                        {
                            var (entry, _) = _nodeModules.Get(dirPath);
                            // Look up the package name for granular updates
                            var packageName = entry!.Value().Paths is { } paths && paths.TryGetValue(path, out var name) ? name : "";
                            entry.Change(bucket => bucket.MarkNodeModulesDirty(packageName));
                            if (!entry.Value()._state._multipleFilesDirty)
                            {
                                cleanNodeModulesBuckets.Remove(dirPath);
                            }
                        }
                    }
                    else
                    {
                        // Check if this path (possibly a realpath of a workspace package) is in any bucket's Paths.
                        // This handles local workspace packages where the realpath doesn't contain /node_modules/.
                        // （见文件头偏离 17：C# 不允许边遍历边删，先收集。）
                        List<Path>? removals = null;
                        foreach (var bucketDirPath in cleanNodeModulesBuckets)
                        {
                            var (entry, _) = _nodeModules.Get(bucketDirPath);
                            if (entry!.Value().Paths is { } paths && paths.TryGetValue(path, out var packageName))
                            {
                                // Use the package name for granular updates
                                entry.Change(bucket => bucket.MarkNodeModulesDirty(packageName));
                                if (!entry.Value()._state._multipleFilesDirty)
                                {
                                    (removals ??= []).Add(bucketDirPath);
                                }
                            }
                        }

                        if (removals is not null)
                        {
                            foreach (var removed in removals)
                            {
                                cleanNodeModulesBuckets.Remove(removed);
                            }
                        }
                    }
                }

                // For projects, mark the bucket dirty if the bucket contains the file directly.
                // Any other significant change, like a created failed lookup location, is
                // handled by newProgramStructure.
                {
                    List<ProjectID>? removals = null;
                    foreach (var projectDirPath in cleanProjectBuckets)
                    {
                        var (entry, _) = _projects.Get(projectDirPath);
                        if (entry!.Value().Paths is { } paths && paths.ContainsKey(path))
                        {
                            // Project buckets don't use package-based granular updates
                            entry.Change(bucket => bucket.MarkProjectFileDirty(path));
                            if (!entry.Value()._state._multipleFilesDirty)
                            {
                                (removals ??= []).Add(projectDirPath);
                            }
                        }
                    }

                    if (removals is not null)
                    {
                        foreach (var removed in removals)
                        {
                            cleanProjectBuckets.Remove(removed);
                        }
                    }
                }
            }
        }

        MarkFilesDirty(change.Created);
        MarkFilesDirty(change.Deleted);
        MarkFilesDirty(change.Changed);
    }
}

/// <summary>Go: <c>type failedAmbientModuleLookupSource struct</c>（registry.go:1157-1161）。
/// Go 的 <c>mu sync.Mutex</c> 从未被使用 → 不移植（见文件头偏离 15）。</summary>
internal sealed class FailedAmbientModuleLookupSource
{
    internal string _fileName = "";

    internal string _packageName = "";
}

/// <summary>Go: <c>type bucketBuildResult struct</c>（registry.go:1163-1182）。</summary>
internal sealed class BucketBuildResult
{
    internal Action<RegistryBucket> _replaceBucket = null!;

    internal Path _resolutionPath;

    internal Exception? _err;

    internal RegistryBucket? _bucket;

    /// <summary>本桶各包解析出的 entrypoints，稍后并入 registry 级 entrypoints 表。</summary>
    internal Dictionary<Path, ResolvedEntrypoint[]>? _entrypoints;

    /// <summary>粒度更新时要先删掉的 registry 级 entrypoint 路径。</summary>
    internal List<Path> _removedEntrypointPaths = [];

    internal SyncMap<Path, FailedAmbientModuleLookupSource>? _possibleFailedAmbientModuleLookupSources;

    internal SyncSet<string>? _possibleFailedAmbientModuleLookupTargets;
}

/// <summary>Go 的 <c>autoimport</c> 包的包级函数/变量（见文件头偏离 5）。</summary>
public static partial class AutoImport
{
    /// <summary>Go: <c>var knownRecursiveSearchPackages = collections.NewSetFromItems(...)</c>（registry.go:38-85）。</summary>
    internal static readonly Set<string> KnownRecursiveSearchPackages = NewSetFromItems(
        "@material-ui/core",
        "@material-ui/icons",
        "@sap/cds",
        "@testing-library/react-native",
        "ajv",
        "asap",
        "async",
        "aws-sdk",
        "braintree-web",
        "core-js",
        "core-js-pure",
        "crypto-js",
        "cypress-mochawesome-reporter",
        "dd-trace",
        "dumi",
        "dva",
        "egg-mock",
        "electron-log",
        "es-abstract",
        "es6-promise",
        "eslint-config-taro",
        "expo",
        "expo-router",
        "flow-remove-types",
        "gatsby",
        "glamor",
        "gluegun",
        "graphology-indices",
        "graphology-traversal",
        "graphology-utils",
        "jest-expo",
        "lodash",
        "lodash-es",
        "moment",
        "mz",
        "next",
        "pdfjs-dist",
        "protobufjs",
        "react-app-polyfill",
        "react-dev-utils",
        "react-devtools-inline",
        "recast",
        "semver",
        "stylelint-config-html",
        "umi",
        "web3-provider-engine",
        "webpack");

    /// <summary>Go: <c>func bucketBuildPreferencesFromUserPreferences(prefs lsutil.UserPreferences) bucketBuildPreferences</c>
    /// （registry.go:101-106）。</summary>
    internal static BucketBuildPreferences BucketBuildPreferencesFromUserPreferences(UserPreferences prefs) => new()
    {
        _fileExcludePatterns = prefs.AutoImportFileExcludePatterns,
        _autoImportEntrypointDirectorySearch = prefs.AutoImportEntrypointDirectorySearch,
    };

    /// <summary>Go: <c>func recursiveSearchSubset(target, current *collections.Set[string]) bool</c>（registry.go:222-233）。
    /// nil 代表"所有包"（任何具体集合的超集）。</summary>
    internal static bool RecursiveSearchSubset(Set<string>? target, Set<string>? current)
    {
        if (target is null)
        {
            // Target wants all packages searched — only satisfied if current is also all.
            return current is null;
        }

        if (current is null)
        {
            // Current searched all packages, so any concrete target is satisfied.
            return true;
        }

        return target.IsSubsetOf(current);
    }

    /// <summary>Go: <c>func NewRegistry(toPath func(fileName string) tspath.Path, preferences lsutil.UserPreferences) *Registry</c>
    /// （registry.go:346-353）。</summary>
    public static Registry NewRegistry(Func<string, Path> toPath, UserPreferences preferences) => new()
    {
        _toPath = toPath,
        _userPreferences = preferences,
        _directories = [],
    };

    /// <summary>Go: <c>func newRegistryBucket() *RegistryBucket</c>（registry.go:288-296）。</summary>
    internal static RegistryBucket NewRegistryBucket() => new()
    {
        _state = new BucketState
        {
            _multipleFilesDirty = true,
            _newProgramStructure = NewProgramStructure.DifferentFileNames,
        },
    };

    /// <summary>Go: <c>func newRegistryBuilder(registry *Registry, host RegistryCloneHost) *registryBuilder</c>（registry.go:527-546）。</summary>
    internal static RegistryBuilder NewRegistryBuilder(Registry registry, RegistryCloneHost host) => new()
    {
        _host = host,
        _base = registry,

        _userPreferences = registry._userPreferences,
        _directories = NewMap(registry._directories),
        _nodeModules = NewMap(registry._nodeModules),
        _projects = NewMap(registry._projects),
        _specifierCache = NewMapBuilder<Path, SyncMap<Path, string>, SyncMap<Path, string>>(
            registry._specifierCache,
            Identity,
            Identity),
        _uniquePackageCount = registry._uniquePackageCount,
        _entrypoints = NewMapBuilder<Path, ResolvedEntrypoint[], ResolvedEntrypoint[]>(
            registry._entrypoints,
            Identity,
            Identity),
    };

    /// <summary>Go: <c>func hasNewNonNodeModulesFiles(program *compiler.Program, bucket *RegistryBucket) bool</c>
    /// （registry.go:1120-1136）。</summary>
    internal static bool HasNewNonNodeModulesFiles(TypeScript.Compiler.Program program, RegistryBucket bucket)
    {
        if (bucket._state._newProgramStructure != NewProgramStructure.DifferentFileNames)
        {
            return false;
        }

        foreach (var file in program.GetSourceFiles())
        {
            if (file.IsContentMapperSupplemental() ||
                file.FileName().Contains("/node_modules/", StringComparison.Ordinal) ||
                IsIgnoredFile(program, file))
            {
                continue;
            }

            if (bucket.Paths is null || !bucket.Paths.ContainsKey(file.Path()))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Go: <c>func isIgnoredFile(program *compiler.Program, file *ast.SourceFile) bool</c>（registry.go:1138-1140）。</summary>
    internal static bool IsIgnoredFile(TypeScript.Compiler.Program program, SourceFile file) =>
        program.IsSourceFileDefaultLibrary(file.Path()) || program.IsGlobalTypingsFile(file.FileName());

    /// <summary>Go: <c>func hasSymlinkToNodeModules(filePath, projectRootPath tspath.Path, symlinkCache *symlinks.KnownSymlinks) bool</c>
    /// （registry.go:1142-1189）。</summary>
    internal static bool HasSymlinkToNodeModules(Path filePath, Path projectRootPath, KnownSymlinks? symlinkCache)
    {
        if (symlinkCache is null)
        {
            return false;
        }

        // Keep files inside this project indexed in project buckets even if they are
        // reachable through a node_modules symlink from elsewhere.
        if (projectRootPath.ContainsPath(filePath))
        {
            return false;
        }

        // First check if the file itself has a symlink to node_modules
        var filesByRealpath = symlinkCache.FilesByRealpath();
        if (filesByRealpath is not null)
        {
            var (symlinkPaths, ok) = filesByRealpath.Load(filePath);
            if (ok && symlinkPaths is not null)
            {
                var found = false;
                symlinkPaths.Range(symlinkPath =>
                {
                    if (symlinkPath.Contains("/node_modules/", StringComparison.Ordinal))
                    {
                        found = true;
                        return false; // stop ranging
                    }

                    return true;
                });

                if (found)
                {
                    return true;
                }
            }
        }

        // Fall back to checking ancestor directories
        var directoriesByRealpath = symlinkCache.DirectoriesByRealpath();
        if (directoriesByRealpath is null)
        {
            return false;
        }

        var ancestorFound = false;
        filePath.ForEachAncestorDirectory<object?>(dirPath =>
        {
            var (symlinkPaths, ok) = directoriesByRealpath.Load(dirPath.EnsureTrailingDirectorySeparator());
            if (!ok || symlinkPaths is null)
            {
                return (null, false);
            }

            // Check if any of the symlinks point to a node_modules directory
            symlinkPaths.Range(symlinkPath =>
            {
                if (symlinkPath.Contains("/node_modules/", StringComparison.Ordinal))
                {
                    ancestorFound = true;
                    return false; // stop ranging
                }

                return true;
            });

            return (null, ancestorFound); // stop if we found a match
        }, out _);

        return ancestorFound;
    }

    /// <summary>Go: <c>func installExtractions(discovered []*discoveredPackage, extractionCache map[string]*perPackageExtractionResult) *packageExtractionResult</c>
    /// （registry.go:1645-1691）。</summary>
    internal static PackageExtractionResult InstallExtractions(
        List<DiscoveredPackage> discovered,
        Dictionary<string, PerPackageExtractionResult> extractionCache)
    {
        var result = new PackageExtractionResult();

        foreach (var pkg in discovered)
        {
            var extraction = extractionCache.TryGetValue(pkg._realpath, out var mainExtraction)
                ? mainExtraction
                : (extractionCache.TryGetValue(pkg._typesRealpath, out var typesExtraction) ? typesExtraction : null);
            if (extraction is null)
            {
                continue;
            }

            foreach (var (path, exports) in extraction._exports)
            {
                result._exports[path] = exports;
            }

            if (!result._packageFiles.TryGetValue(pkg._packageName, out var packageFiles) || packageFiles is null)
            {
                packageFiles = new Dictionary<Path, string>(extraction._packageFiles.Count);
                result._packageFiles[pkg._packageName] = packageFiles;
            }

            foreach (var (path, fileName) in extraction._packageFiles)
            {
                packageFiles[path] = fileName;
            }

            foreach (var (name, fileNames) in extraction._ambientModules)
            {
                result._ambientModuleNames[name] = result._ambientModuleNames.TryGetValue(name, out var existing)
                    ? [.. existing, .. fileNames]
                    : fileNames;
            }

            if (extraction._entrypoints is not null)
            {
                result._entrypoints.Add(extraction._entrypoints);
            }

            foreach (var (path, source) in extraction._failedAmbientModuleLookupSources)
            {
                result._possibleFailedAmbientModuleLookupSources.LoadOrStore(path, source);
            }

            foreach (var target in extraction._failedAmbientModuleLookupTargets.Keys())
            {
                result._possibleFailedAmbientModuleLookupTargets.Add(target);
            }

            if (extraction._isSymlinked && pkg._isLocal)
            {
                result._workspacePackages.Add(pkg._packageName);
            }

            result._stats.AddExports(extraction._statsExports);
            result._stats.AddUsedChecker(extraction._statsUsedChecker);
            result._skippedEntrypointsCount += extraction._skippedEntrypoints;
        }

        return result;
    }
}
