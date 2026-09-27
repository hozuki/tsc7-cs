// ported from: internal/compiler/projectreferenceparser.go
//
// Go→C# 改写（逐条，都不改变语义）：
// 1) Go 的 `defer tr.Push(...)()` → `var traceDone = ...Push(...); try { … } finally { traceDone?.Invoke(); }`
//    （C# 没有 defer；`Tracing?.Push` 对应 Go 的 nil 接收者安全检查，`Push` 返回 `Action`）。
// 2) `core.Map` → `Map`（`using static TypeScript.Core.Core;`）；`maps.Copy(dst, src)` → 逐键赋值循环
//    （Go 对 nil src map 是 no-op，C# 侧加 `is not null` 守卫）。
// 3) 包级函数 `createProjectReferenceParseTasks` → `Compiler` 静态类（见 Compiler.cs 文件头）。
// 4) `collections.Set[*projectReferenceParseTask]` → `Set<ProjectReferenceParseTask>`（引用相等，与 Go 的指针相等一致）。
namespace TypeScript.Compiler;

using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.Module;
using TypeScript.TSPath;
using TypeScript.TsOptions;
using static TypeScript.Core.Core;
using Path = TypeScript.TSPath.Path;
using TracingPhase = TypeScript.Tracing.Phase;

/// <summary>Go: <c>type projectReferenceParseTask struct</c>。</summary>
internal sealed class ProjectReferenceParseTask
{
    internal string ConfigName = "";
    internal ParsedCommandLine? Resolved;
    internal ProjectReferenceParseTask[] _subTasks = [];

    internal void Parse(ProjectReferenceParser projectReferenceParser)
    {
        var loader = projectReferenceParser._loader;
        var traceDone = loader._opts.Tracing?.Push(TracingPhase.Parse, "parseJsonSourceFileConfigFileContent", new Dictionary<string, object?> { ["path"] = ConfigName }, false);
        try
        {
            Resolved = loader._opts.Host.GetResolvedProjectReference(ConfigName, loader.ToPath(ConfigName));
            if (Resolved is null)
            {
                return;
            }

            Resolved.ParseInputOutputNames();
            var subReferences = Resolved.ResolvedProjectReferencePaths();
            if (subReferences.Length > 0)
            {
                _subTasks = Compiler.CreateProjectReferenceParseTasks(subReferences);
            }
        }
        finally
        {
            traceDone?.Invoke();
        }
    }
}

/// <summary>Go: <c>type projectReferenceParser struct</c>。</summary>
internal sealed class ProjectReferenceParser
{
    internal FileLoader _loader = null!;
    internal WorkGroup _wg = null!;
    internal readonly SyncMap<Path, ProjectReferenceParseTask> _tasksByFileName = new();

    internal void Parse(ProjectReferenceParseTask[] tasks)
    {
        _loader._projectReferenceFileMapper!._loader = _loader;
        Start(tasks);
        _wg.RunAndWait();
        InitMapper(tasks);
    }

    internal void Start(ProjectReferenceParseTask[] tasks)
    {
        for (var i = 0; i < tasks.Length; i++)
        {
            var task = tasks[i];
            var path = _loader.ToPath(task.ConfigName);
            var (loadedTask, loaded) = _tasksByFileName.LoadOrStore(path, task);
            if (loaded)
            {
                // dedup tasks to ensure correct file order, regardless of which task would be started first
                tasks[i] = loadedTask!;
            }
            else
            {
                _wg.Queue(() =>
                {
                    task.Parse(this);
                    Start(task._subTasks);
                });
            }
        }
    }

    internal void InitMapper(ProjectReferenceParseTask[] tasks)
    {
        var mapper = _loader._projectReferenceFileMapper!;
        var totalReferences = _tasksByFileName.Size() + 1;
        mapper._configToProjectReference = new Dictionary<Path, ParsedCommandLine?>(totalReferences);
        mapper._referencesInConfigFile = new Dictionary<Path, Path[]>(totalReferences);
        mapper._sourceToProjectReference = new Dictionary<Path, SourceOutputAndProjectReference>();
        mapper._outputDtsToProjectReference = new Dictionary<Path, SourceOutputAndProjectReference>();
        mapper._referencesInConfigFile[mapper.RootConfigPath()] = InitMapperWorker(tasks, new Set<ProjectReferenceParseTask>());
        if (mapper._opts.CanUseProjectReferenceSource() && mapper._outputDtsToProjectReference.Count != 0)
        {
            mapper._host = Compiler.NewProjectReferenceDtsFakingHost(_loader);
        }
    }

    internal Path[] InitMapperWorker(ProjectReferenceParseTask[] tasks, Set<ProjectReferenceParseTask> seen)
    {
        if (tasks.Length == 0)
        {
            return [];
        }

        var mapper = _loader._projectReferenceFileMapper!;
        var results = new List<Path>(tasks.Length);
        foreach (var task in tasks)
        {
            var path = _loader.ToPath(task.ConfigName);
            results.Add(path);

            // ensure we only walk each task once
            if (!seen.AddIfAbsent(task))
            {
                continue;
            }

            mapper._configToProjectReference[path] = task.Resolved;
            if (task.Resolved is not null && mapper._opts.Config.ConfigFile != task.Resolved.ConfigFile)
            {
                // Map current task's files first, before recursing into subtasks.
                // This matches TypeScript's behavior where child project references
                // overwrite parent entries when a file belongs to multiple projects.
                if (task.Resolved.SourceToProjectReference() is { } sourceToProjectReference)
                {
                    foreach (var (key, value) in sourceToProjectReference)
                    {
                        mapper._sourceToProjectReference[key] = value;
                    }
                }

                if (task.Resolved.OutputDtsToProjectReference() is { } outputDtsToProjectReference)
                {
                    foreach (var (key, value) in outputDtsToProjectReference)
                    {
                        mapper._outputDtsToProjectReference[key] = value;
                    }
                }

                if (mapper._opts.CanUseProjectReferenceSource())
                {
                    var declDir = task.Resolved.CompilerOptions()!.DeclarationDir;
                    if (declDir == "")
                    {
                        declDir = task.Resolved.CompilerOptions()!.OutDir;
                    }

                    if (declDir != "")
                    {
                        _loader._dtsDirectories.Add(_loader.ToPath(declDir));
                    }
                }
            }

            var referencesInConfig = InitMapperWorker(task._subTasks, seen);
            mapper._referencesInConfigFile[path] = referencesInConfig;
        }

        return results.ToArray();
    }
}

public static partial class Compiler
{
    /// <summary>Go: <c>func createProjectReferenceParseTasks(projectReferences []string) []*projectReferenceParseTask</c>。</summary>
    internal static ProjectReferenceParseTask[] CreateProjectReferenceParseTasks(string[] projectReferences) =>
        Map(projectReferences, configName => new ProjectReferenceParseTask { ConfigName = configName });
}
