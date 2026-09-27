// ported from: internal/project/projectcollection.go:1-391
//
// 与 Go 的偏离（逐条说明）：
//
// 1) **包级函数落点**：Go `project` 包与类型 `Project` 同名 ⇒ 不能建 `static class Project`（CS0101）。
//    包级函数挂到最相关类型的静态成员上（先例 `Locale.Parse`/`Locale.WithLocale`）：
//      `openFilePaths` → `ProjectCollection.OpenFilePaths`；
//      `findDefaultConfiguredProjectFromProgramInclusion` → `ProjectCollection.FindDefaultConfiguredProjectFromProgramInclusion`。
//
// 2) Go 的 `map[tspath.Path]ID` / `map[ConfiguredProjectID]*Project` / `collections.Set[tspath.Path]` →
//    `Dictionary<Path, ID>` / `Dictionary<ConfiguredProjectID, Project>` / `Collections.Set<Path>`。
//    这些字段在 Go 里是**值语义**（map 是引用但按"不可变快照"约定使用），C# 侧用可变容器 + `Clone()` 的浅拷贝
//    语义（`ProjectCollection.Clone()` 只复制引用，与 Go 的 `clone()` 逐字段照抄一致）。
//
// 3) `ID`/`ConfiguredProjectID`/`SyntheticProjectID` 是 `readonly record struct`（值相等），可以安全做字典键
//    （见 Project.cs 文件头第 3 条；`autoimport` 的 `map[ProjectID]X` 依赖值相等）。
//
// 4) `sync.Once`（`openConfiguredProjectsOnce`）→ “标志 + `System.Threading.Lock` 双重检查”（仓内既有做法）。
//    Go 的 `openConfiguredProjectsOnce.Do(func(){ … })` 里对 `c.openConfiguredProjects` 赋值 ⇒ C# 用
//    `_openConfiguredProjects` 可空字段 + 双检锁。
//
// 5) `maps.Clone(s.openProjects)` → `new Dictionary<>(s._openProjects)`（返回新实例，元素按引用共享，与 Go 一致）；
//    `maps.Equal(a, b)` → `Equals` 里的逐项比较（Go 的 `maps.Equal` 语义：长度相同且每个键值相等）。
//
// 6) `ID()`/`Configured()`/`Inferred()`/`Synthetic()` 这几个 Go 方法（`(T,bool)` 形态）在 C# 里是
//    `TryX(out T)`（见 Project.cs：`id.TryConfigured(out var configuredID)` 等）。
//
// 7) `core.BreadthFirstSearchParallelEx(..., core.Identity)` → `BreadthFirstSearchParallelEx<Project, Project>(...)`
//    配 `BreadthFirstSearchOptions<Project, Project>`（`K`/`N` 都取引用类型 `Project`，Go 里 K 是 `*Project`）
//    与 identity 函数 `static p => p`（C# 的 `Core.Identity` 也可，但显式 lambda 更省一次泛型推断）。
//
// 8) `configFileRegistry.GetConfigFileName(path)` / `GetConfig(path)` / `GetAncestorConfigFileName(path, name)`
//    是 `ConfigFileRegistry` 的导出方法（C 组的 `ConfigFileRegistry.cs`），名字照 Go。
using Path = TypeScript.TSPath.Path;
using static TypeScript.Collections.Collections;
using static TypeScript.Core.Core;

namespace TypeScript.Project;

public sealed class ProjectCollection
{
    internal Func<string, Path> _toPath = null!;
    internal ConfigFileRegistry _configFileRegistry = null!;

    /// <summary>
    /// fileDefaultProjects is a map of file paths to the ID of the default project
    /// for that file. This map contains quick lookups for only the associations discovered during the latest
    /// snapshot update.
    /// </summary>
    internal Dictionary<Path, ID> _fileDefaultProjects = [];

    /// <summary>
    /// configuredProjects is the set of loaded projects associated with a tsconfig
    /// file, keyed by the config file path.
    /// </summary>
    internal Dictionary<ConfiguredProjectID, Project> _configuredProjects = [];

    /// <summary>syntheticProjects contains synthetic projects created explicitly through the API。</summary>
    internal Dictionary<SyntheticProjectID, Project> _syntheticProjects = [];

    /// <summary>
    /// openFiles is the set of open file paths associated with the snapshot that owns
    /// this project collection.
    /// </summary>
    internal Collections.Set<Path> _openFiles = new();

    /// <summary>inferredProject is a fallback project that is used when no configured project can be found for an open file。</summary>
    internal Project? _inferredProject;

    /// <summary>
    /// apiState tracks the projects and files that API clients have explicitly
    /// opened so they are kept loaded across snapshots.
    /// </summary>
    internal APIState _apiState = new();

    // Go: openConfiguredProjectsOnce sync.Once（见文件头第 4 条）
    private readonly Lock _openConfiguredProjectsOnceLock = new();
    private Collections.Set<ConfiguredProjectID>? _openConfiguredProjects;

    public ConfigFileRegistry ConfigFileRegistry()
    {
        return _configFileRegistry;
    }

    public Project? ConfiguredProject(Path path)
    {
        return _configuredProjects.GetValueOrDefault(new ConfiguredProjectID(path));
    }

    public Project? GetProject(ID id)
    {
        if (id.TryInferred(out _))
        {
            return _inferredProject;
        }

        if (id.TrySynthetic(out var syntheticID))
        {
            return _syntheticProjects.GetValueOrDefault(syntheticID);
        }

        if (id.TryConfigured(out var configuredID))
        {
            return _configuredProjects.GetValueOrDefault(configuredID);
        }

        return null;
    }

    /// <summary>ConfiguredProjects returns all configured projects in a stable order。</summary>
    public Project[] ConfiguredProjects()
    {
        var projects = new List<Project>(_configuredProjects.Count);
        FillConfiguredProjects(projects);
        return [.. projects];
    }

    private void FillConfiguredProjects(List<Project> projects)
    {
        foreach (var p in _configuredProjects.Values)
        {
            projects.Add(p);
        }

        projects.Sort(static (a, b) => string.CompareOrdinal(a.ID().Value, b.ID().Value));
    }

    /// <summary>SyntheticProjects returns all synthetic projects in a stable order。</summary>
    public Project[] SyntheticProjects()
    {
        var projects = new List<Project>(_syntheticProjects.Count);
        foreach (var project in _syntheticProjects.Values)
        {
            projects.Add(project);
        }

        projects.Sort(static (a, b) => string.CompareOrdinal(a.ID().Value, b.ID().Value));
        return [.. projects];
    }

    /// <summary>ProjectsByID returns all projects keyed by project ID in stable order。</summary>
    public Collections.OrderedMap<ID, Project> ProjectsByID()
    {
        var projects = NewOrderedMapWithSizeHint<ID, Project>(
            _configuredProjects.Count + _syntheticProjects.Count + (_inferredProject is not null ? 1 : 0));
        foreach (var project in ConfiguredProjects())
        {
            projects.Set(project.ID(), project);
        }

        foreach (var project in SyntheticProjects())
        {
            projects.Set(project.ID(), project);
        }

        if (_inferredProject is not null)
        {
            projects.Set(_inferredProject.ID(), _inferredProject);
        }

        return projects;
    }

    /// <summary>Projects returns all configured, synthetic, and inferred projects in a stable order。</summary>
    public Project[] Projects()
    {
        var projects = new List<Project>(_configuredProjects.Count + _syntheticProjects.Count + (_inferredProject is not null ? 1 : 0));
        FillConfiguredProjects(projects);
        projects.AddRange(SyntheticProjects());
        if (_inferredProject is not null)
        {
            projects.Add(_inferredProject);
        }

        return [.. projects];
    }

    /// <summary>
    /// LanguageServiceProjects returns configured and inferred projects in stable order.
    /// Synthetic projects are accessed explicitly through the API and do not participate
    /// in cross-project language service operations.
    /// </summary>
    public Project[] LanguageServiceProjects()
    {
        var projects = new List<Project>(_configuredProjects.Count + (_inferredProject is not null ? 1 : 0));
        FillConfiguredProjects(projects);
        if (_inferredProject is not null)
        {
            projects.Add(_inferredProject);
        }

        return [.. projects];
    }

    public Project? InferredProject()
    {
        return _inferredProject;
    }

    /// <summary>
    /// GetLanguageServiceProjectsContainingFile does not consider synthetic projects
    /// (ones created by API via createProgram).
    /// </summary>
    public Ls.Project[] GetLanguageServiceProjectsContainingFile(Path path)
    {
        var projects = new List<Ls.Project>();
        foreach (var project in ConfiguredProjects())
        {
            if (project.ContainsFile(path))
            {
                projects.Add(project);
            }
        }

        if (_inferredProject is not null && _inferredProject.ContainsFile(path))
        {
            projects.Add(_inferredProject);
        }

        return [.. projects];
    }

    /// <summary>GetOpenConfiguredProjects returns configured projects containing at least one open file。</summary>
    public Collections.Set<ConfiguredProjectID> GetOpenConfiguredProjects()
    {
        lock (_openConfiguredProjectsOnceLock)
        {
            if (_openConfiguredProjects is null)
            {
                var openProjects = NewSetWithSizeHint<ConfiguredProjectID>(_configuredProjects.Count);
                foreach (var path in _openFiles.M)
                {
                    if (_fileDefaultProjects.TryGetValue(path, out var projectID))
                    {
                        if (projectID.TryConfigured(out var configuredID) && _configuredProjects.ContainsKey(configuredID))
                        {
                            openProjects.Add(configuredID);
                            continue;
                        }
                    }

                    foreach (var project in _configuredProjects.Values)
                    {
                        if (project.ContainsFile(path))
                        {
                            project.ID().TryConfigured(out var configuredID);
                            openProjects.Add(configuredID);
                        }
                    }
                }

                _openConfiguredProjects = openProjects;
            }
        }

        return _openConfiguredProjects;
    }

    /// <summary>Go: <c>func openFilePaths(overlays map[tspath.Path]*Overlay) collections.Set[tspath.Path]</c>。</summary>
    public static Collections.Set<Path> OpenFilePaths(Dictionary<Path, Overlay> overlays)
    {
        var openFiles = new Collections.Set<Path>();
        foreach (var path in overlays.Keys)
        {
            openFiles.Add(path);
        }

        return openFiles;
    }

    /// <summary>!!! result could be cached。</summary>
    public Project? GetDefaultProject(Path path)
    {
        if (_fileDefaultProjects.TryGetValue(path, out var result))
        {
            if (result.TryInferred(out _))
            {
                return _inferredProject;
            }

            result.TryConfigured(out var configuredID);
            return _configuredProjects.GetValueOrDefault(configuredID);
        }

        var containingProjects = new List<Project>();
        Project? firstConfiguredProject = null;
        Project? firstNonSourceOfProjectReferenceRedirect = null;
        var multipleDirectInclusions = false;
        foreach (var p in ConfiguredProjects())
        {
            if (p.ContainsFile(path))
            {
                containingProjects.Add(p);
                if (!multipleDirectInclusions && !p.IsSourceFromProjectReference(path))
                {
                    if (firstNonSourceOfProjectReferenceRedirect is null)
                    {
                        firstNonSourceOfProjectReferenceRedirect = p;
                    }
                    else
                    {
                        multipleDirectInclusions = true;
                    }
                }

                firstConfiguredProject ??= p;
            }
        }

        if (containingProjects.Count == 1)
        {
            return containingProjects[0];
        }

        if (containingProjects.Count == 0)
        {
            if (_inferredProject is not null && _inferredProject.ContainsFile(path))
            {
                return _inferredProject;
            }

            return null;
        }

        if (!multipleDirectInclusions)
        {
            if (firstNonSourceOfProjectReferenceRedirect is not null)
            {
                // Multiple projects include the file, but only one is a direct inclusion.
                return firstNonSourceOfProjectReferenceRedirect;
            }

            // Multiple projects include the file, and none are direct inclusions.
            return firstConfiguredProject;
        }

        // Multiple projects include the file directly.
        var defaultProject = FindDefaultConfiguredProject(path);
        if (defaultProject is not null)
        {
            return defaultProject;
        }

        return firstConfiguredProject;
    }

    private Project? FindDefaultConfiguredProject(Path path)
    {
        var configFileName = _configFileRegistry.GetConfigFileName(path);
        if (configFileName.Length != 0)
        {
            return FindDefaultConfiguredProjectWorker(path, configFileName, visited: null, fallback: null);
        }

        return null;
    }

    private Project? FindDefaultConfiguredProjectWorker(
        Path path,
        string configFileName,
        Collections.SyncSet<Project>? visited,
        Project? fallback)
    {
        var configFilePath = _toPath(configFileName);
        if (!_configuredProjects.TryGetValue(new ConfiguredProjectID(configFilePath), out var project))
        {
            return null;
        }

        visited ??= new Collections.SyncSet<Project>();

        // Look in the config's project and its references recursively.
        var search = BreadthFirstSearchParallelEx<Project, Project>(
            project,
            project =>
            {
                if (project.CommandLine is null)
                {
                    return [];
                }

                // A referenced project may not be loaded if `disableReferencedProjectLoad` is true.
                // `Core.MapNonNil` 会过滤掉 nil（Go 的 `core.MapNonNil` 语义），故这里用 `!` 让元素类型保持非空。
                return MapNonNil(
                    project.CommandLine.ResolvedProjectReferencePaths(),
                    configFileName => _configuredProjects.GetValueOrDefault(new ConfiguredProjectID(_toPath(configFileName)))!);
            },
            project =>
            {
                if (project.ContainsFile(path))
                {
                    return (true, !project.IsSourceFromProjectReference(path));
                }

                return (false, false);
            },
            new Core.BreadthFirstSearchOptions<Project, Project> { Visited = visited },
            static p => p);

        if (search.Stopped)
        {
            // If we found a project that directly contains the file, return it.
            return search.Path![0]!;
        }

        if (search.Path is { Length: > 0 } && fallback is null)
        {
            // If we found a project that contains the file, but it is a source from
            // a project reference, record it as a fallback.
            fallback = search.Path![0]!;
        }

        // Look for tsconfig.json files higher up the directory tree and do the same. This handles
        // the common case where a higher-level "solution" tsconfig.json contains all projects in a
        // workspace.
        var config = _configFileRegistry.GetConfig(path);
        if (config is not null && config.CompilerOptions()!.DisableSolutionSearching.IsTrue())
        {
            return fallback;
        }

        var ancestorConfigName = _configFileRegistry.GetAncestorConfigFileName(path, configFileName);
        if (ancestorConfigName.Length != 0)
        {
            return FindDefaultConfiguredProjectWorker(path, ancestorConfigName, visited, fallback);
        }

        return fallback;
    }

    /// <summary>clone creates a shallow copy of the project collection。</summary>
    internal ProjectCollection Clone()
    {
        return new ProjectCollection
        {
            _toPath = _toPath,
            _configFileRegistry = _configFileRegistry,
            _configuredProjects = _configuredProjects,
            _syntheticProjects = _syntheticProjects,
            _openFiles = _openFiles,
            _inferredProject = _inferredProject,
            _fileDefaultProjects = _fileDefaultProjects,
            _apiState = _apiState,
        };
    }

    /// <summary>
    /// findDefaultConfiguredProjectFromProgramInclusion finds the default configured project for a file
    /// based on the file's inclusion in existing projects. The projects should be sorted, as ties will
    /// be broken by slice order. <paramref name="getProject"/> should return a project with an up-to-date program.
    /// Along with the resulting project path, a boolean is returned indicating whether there were multiple
    /// direct inclusions of the file in different projects, indicating that the caller may want to perform
    /// additional logic to determine the best project.
    /// </summary>
    public static (Path Result, bool MultipleCandidates) FindDefaultConfiguredProjectFromProgramInclusion(
        string fileName,
        Path path,
        Path[] projectPaths,
        Func<Path, Project> getProject)
    {
        var containingProjects = new List<Path>();
        var firstConfiguredProject = default(Path);
        var firstNonSourceOfProjectReferenceRedirect = default(Path);
        var multipleDirectInclusions = false;

        foreach (var projectPath in projectPaths)
        {
            var p = getProject(projectPath);
            if (p.ContainsFile(path))
            {
                containingProjects.Add(projectPath);
                if (!multipleDirectInclusions && !p.IsSourceFromProjectReference(path))
                {
                    if (firstNonSourceOfProjectReferenceRedirect == default)
                    {
                        firstNonSourceOfProjectReferenceRedirect = projectPath;
                    }
                    else
                    {
                        multipleDirectInclusions = true;
                    }
                }

                if (firstConfiguredProject == default)
                {
                    firstConfiguredProject = projectPath;
                }
            }
        }

        if (containingProjects.Count == 1)
        {
            return (containingProjects[0], false);
        }

        if (!multipleDirectInclusions)
        {
            if (firstNonSourceOfProjectReferenceRedirect != default)
            {
                // Multiple projects include the file, but only one is a direct inclusion.
                return (firstNonSourceOfProjectReferenceRedirect, false);
            }

            // Multiple projects include the file, and none are direct inclusions.
            return (firstConfiguredProject, false);
        }

        // Multiple projects include the file directly.
        return (firstConfiguredProject, true);
    }
}

/// <summary>
/// APIState tracks the projects and files that API clients have explicitly opened.
/// Opens and closes are ref-counted so multiple API clients don't clobber each
/// other, and it is carried across snapshots so API-opened resources stay loaded.
/// </summary>
public sealed class APIState
{
    /// <summary>
    /// openProjects is the ref-counted set of projects to keep open for API
    /// clients, keyed by config file path.
    /// </summary>
    internal Dictionary<Path, int> _openProjects = [];

    /// <summary>
    /// openFiles is the ref-counted set of files to keep open for API clients,
    /// keyed by file path. Files with no configured project are loaded into the
    /// inferred project.
    /// </summary>
    internal Dictionary<Path, ApiOpenedFile> _openFiles = [];

    internal APIState Clone()
    {
        return new APIState
        {
            _openProjects = new Dictionary<Path, int>(_openProjects),
            _openFiles = new Dictionary<Path, ApiOpenedFile>(_openFiles),
        };
    }

    internal bool Equals(APIState other)
    {
        // Go: maps.Equal(s.openProjects, other.openProjects) && maps.Equal(s.openFiles, other.openFiles)
        if (_openProjects.Count != other._openProjects.Count || _openFiles.Count != other._openFiles.Count)
        {
            return false;
        }

        foreach (var pair in _openProjects)
        {
            if (!other._openProjects.TryGetValue(pair.Key, out var value) || value != pair.Value)
            {
                return false;
            }
        }

        foreach (var pair in _openFiles)
        {
            if (!other._openFiles.TryGetValue(pair.Key, out var value) || !value.Equals(pair.Value))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>apiOpenedFile tracks a file kept open by API clients along with its ref count。</summary>
public sealed record ApiOpenedFile
{
    internal string FileName = "";

    internal int RefCount;
}
