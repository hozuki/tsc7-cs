// ported from: internal/execute/build/orchestrator.go
//
// 与 Go 的偏离（docs/architecture.md §2/§3）：
// 1) `context.Context` → `TypeScript.Core.Context`；`io.Writer` → `TextWriter`；`fmt.Fprintf(w, "%v\n", err)` →
//    `w.Write(err.Message + "\n")`（Go 的 `%v` 对 error 打印 `Error()`）。
// 2) 未导出字段 → `internal` + `_camelCase`；包级函数 `NewOrchestrator` → 与包同名的静态类 `Build`（§2）。
// 3) **`rangeTasks` 的并发原语**：Go 用 `core.NewWorkGroup(false)` + 若干 goroutine。C# 侧这些"builder"线程要跑
//    parse/bind/check/emit（深递归），所以按 §3.9 必须在**显式指定了大栈的线程**上执行 —— 这里用
//    `Thread(..., maxStackSize: 64MB)`（与 tests/TypeScript.Harness 的常量一致），而不是 `WorkGroup`（线程池栈不可控）。
//    `createBuildTasks` 只解析 tsconfig（浅递归），照 Go 用 `Core.NewWorkGroup`。
// 4) Go 的 `chan struct{}`（`task.built`/`task.done` 的关闭/等待）→ `ManualResetEventSlim`（见 BuildTask.cs）；
//    `atomic.Bool` → `volatile bool` 局部变量 + `ref` 参数（`checkTasksForEventChanges` 的 `*atomic.Bool` 形参）。
// 5) `(T, error)` → 失败抛异常：`ReconcileWatches`/`project.Close()` 的 error 按 Go 的调用点处理（Go 里
//    `_ = Close()` 的地方 C# 用 try/catch 丢弃；`ReconcileWatches` 失败写一行并 `ForceOverflow()`）。
// 6) `slices.SortStableFunc` → `OrderBy`（LINQ 的 OrderBy 是稳定排序，与 Go 一致）；
//    `maps`/`iter.Seq` 的遍历 → `Dictionary`/`IEnumerable` 的 foreach。
// 7) 名字避让（同名命名空间优先于类型，§2）：`Loc`/`TscNs`/`CachedVfs`/`TPath`/`AstDiag` 别名见下。
using global::System;
using global::System.Collections.Generic;
using global::System.IO;
using global::System.Linq;
using global::System.Runtime.ExceptionServices;
using global::System.Threading;
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Compiler;
using TypeScript.ContentMapper;
// 同 Tsc.cs：using 位于 file-scoped namespace 之前 ⇒ 裸写 `Project` 会绑到命名空间 `TypeScript.Project`
// （internal/project 的移植，M8Project 新增）而不是类型 → CS0118。异名别名消歧。
using CmProject = TypeScript.ContentMapper.Project;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.FsWatch;
using TypeScript.TsOptions;
using Loc = TypeScript.Locale.Locale;
using Path = TypeScript.TSPath.Path;
using AstDiag = TypeScript.Ast.Diagnostic;
using Inc = TypeScript.Execute.Incremental;
using TscNs = TypeScript.Execute.Tsc;
using CachedVfs = TypeScript.Vfs.CachedVfs;
using TPath = TypeScript.TSPath.TSPath;
using ComparePathsOptions = TypeScript.TSPath.ComparePathsOptions;
using WM = TypeScript.Execute.WatchManager.WatchManager;
using DirWatchSet = TypeScript.Execute.WatchManager.DirWatchSet;
using CommandLineTestingWithWatchBackend = TypeScript.Execute.WatchManager.CommandLineTestingWithWatchBackend;
using CMProject = TypeScript.ContentMapper.Project;
using static TypeScript.Core.Core;
using static TypeScript.Collections.Collections;
using static TypeScript.TSPath.TSPath;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Execute.Tsc.Tsc;
using static TypeScript.Execute.WatchManager.WatchManager;

namespace TypeScript.Execute.Build;

/// <summary>Go: <c>type Options struct</c>（orchestrator.go:27-31）。</summary>
public sealed class Options
{
    /// <summary>Go: <c>Sys tsc.System</c>。</summary>
    public TscNs.System Sys = null!;

    /// <summary>Go: <c>Command *tsoptions.ParsedBuildCommandLine</c>。</summary>
    public ParsedBuildCommandLine Command = null!;

    /// <summary>Go: <c>Testing tsc.CommandLineTesting</c>（nil = 非测试模式）。</summary>
    public TscNs.CommandLineTesting? Testing;
}

/// <summary>Go: <c>type OrchestratorResult struct</c>（orchestrator.go:32-37）。</summary>
public sealed class OrchestratorResult
{
    /// <summary>Go: <c>Result tsc.CommandLineResult</c>。</summary>
    public TscNs.CommandLineResult Result = new();

    /// <summary>Go: <c>Errors []*ast.Diagnostic</c>。</summary>
    public List<AstDiag> Errors = [];

    /// <summary>Go: <c>Statistics tsc.Statistics</c>（Go 的值类型 → C# 用现成实例）。</summary>
    public TscNs.Statistics Statistics = new();

    /// <summary>Go: <c>FilesToDelete []string</c>。</summary>
    public List<string> FilesToDelete = [];

    /// <summary>Go: <c>func (b *OrchestratorResult) report(o *Orchestrator)</c>（orchestrator.go:39-41）。</summary>
    public void Report(Orchestrator o) => ReportWithFilesToDelete(o, true);

    /// <summary>Go: <c>func (b *OrchestratorResult) reportWithFilesToDelete(o, reportFilesToDelete)</c>（orchestrator.go:43-64）。</summary>
    public void ReportWithFilesToDelete(Orchestrator o, bool reportFilesToDelete)
    {
        if (o._opts.Command.CompilerOptions!.Watch.IsTrue())
        {
            o._watchStatusReporter!(NewCompilerDiagnostic(
                IfElse(Errors.Count == 1, Found_1_error_Watching_for_file_changes, Found_0_errors_Watching_for_file_changes),
                Errors.Count));
        }
        else
        {
            o._errorSummaryReporter!(Errors.ToArray());
        }
        if (reportFilesToDelete && FilesToDelete.Count != 0)
        {
            o.CreateBuilderStatusReporter(null)(NewCompilerDiagnostic(
                A_non_dry_build_would_delete_the_following_files_Colon_0,
                string.Join("", Map(FilesToDelete.ToArray(), f => "\r\n * " + f))));
        }
        if (!o._opts.Command.CompilerOptions.Diagnostics.IsTrue() && !o._opts.Command.CompilerOptions.ExtendedDiagnostics.IsTrue())
        {
            return;
        }
        Statistics.SetTotalTime(o._opts.Sys.SinceStart());
        Statistics.Report(o._opts.Sys.Writer(), o._opts.Testing);
    }
}

/// <summary>Go: <c>type Orchestrator struct</c>（orchestrator.go:66-90）。</summary>
public sealed class Orchestrator : TscNs.Watcher
{
    internal Options _opts = null!;
    internal ComparePathsOptions _comparePathsOptions;
    internal Host _host = null!;

    // contentMapperHost transforms content-mapped files; it is created once per build session (when
    // enabled) and shared across all projects so mapper processes are consolidated. It closes itself when
    // the session context is cancelled (see contentmapper.New).
    internal ContentMapper.Host? _contentMapperHost;

    // order generation result
    internal SyncMap<Path, BuildTask> _tasks = new();
    internal List<string> _order = [];
    internal List<AstDiag> _errors = [];
    internal bool _graphGenerated;

    internal TscNs.DiagnosticsReporter? _errorSummaryReporter;
    internal TscNs.DiagnosticReporter? _watchStatusReporter;

    // fswatch event-based watching
    internal WM _wm = null!;
    // order sorted by dependency depth, to reduce how often builders block on upstream projects
    internal List<string> _scheduleOrder = [];

    /// <summary>Go: <c>func (o *Orchestrator) relativeFileName(fileName string) string</c>（orchestrator.go:93-95）。</summary>
    internal string RelativeFileName(string fileName) => ConvertToRelativePath(fileName, _comparePathsOptions);

    /// <summary>Go: <c>func (o *Orchestrator) toPath(fileName string) tspath.Path</c>（orchestrator.go:97-99）。</summary>
    internal Path ToPath(string fileName) => TPath.ToPath(fileName, _comparePathsOptions.CurrentDirectory, _comparePathsOptions.UseCaseSensitiveFileNames);

    /// <summary>Go: <c>func (o *Orchestrator) resolveBuildInfoFileName(fileName, buildInfoDir string) string</c>（orchestrator.go:101-106）。</summary>
    internal string ResolveBuildInfoFileName(string fileName, string buildInfoDir) =>
        Inc.Incremental.IsBuildInfoFileNameDefaultLibrary(fileName)
            ? CombinePaths(_host.DefaultLibraryPath(), fileName)
            : GetNormalizedAbsolutePath(fileName, buildInfoDir);

    /// <summary>Go: <c>func (o *Orchestrator) Order() []string</c>（orchestrator.go:108-110）。</summary>
    public string[] Order() => _order.ToArray();

    /// <summary>Go: <c>func (o *Orchestrator) ScheduleOrder() []string</c>（orchestrator.go:112-114）。</summary>
    public string[] ScheduleOrder() => _scheduleOrder.ToArray();

    // computeScheduleOrder sorts the build order by dependency depth (projects with no
    // upstream first, then their dependents, and so on). Builders take projects from this
    // order and block until upstream projects are done, so with the plain depth-first order
    // a builder that picks the root of a long chain sits idle while another builder works
    // through the chain, even when unrelated projects are ready to build. Depth order reduces
    // that avoidable blocking but does not eliminate it: a shallower project that has been
    // picked up may not be done yet, so a builder can take a dependent of a slow project and
    // wait on that project while a later project's upstream has already finished. The stable
    // sort preserves the original order within a depth, and reporting still follows Order().
    /// <summary>Go: <c>func (o *Orchestrator) computeScheduleOrder() []string</c>（orchestrator.go:126-148）。</summary>
    internal List<string> ComputeScheduleOrder()
    {
        var depths = new Dictionary<BuildTask, int>(_order.Count);
        var entries = new List<(string Config, int Depth)>(_order.Count);
        foreach (var config in _order)
        {
            var task = GetTask(ToPath(config));
            var depth = 0;
            foreach (var upstream in task._upStream)
            {
                depth = Math.Max(depth, depths[upstream._task] + 1);
            }
            depths[task] = depth;
            entries.Add((config, depth));
        }
        // Go: slices.SortStableFunc(entries, func(a, b) int { return a.depth - b.depth })
        return entries.OrderBy(static entry => entry.Depth).Select(static entry => entry.Config).ToList();
    }

    /// <summary>Go: <c>func (o *Orchestrator) Upstream(configName string) []string</c>（orchestrator.go:150-156）。</summary>
    public string[] Upstream(string configName)
    {
        var path = ToPath(configName);
        var task = GetTask(path);
        return Map(task._upStream.ToArray(), static t => t._task._config);
    }

    /// <summary>Go: <c>func (o *Orchestrator) Downstream(configName string) []string</c>（orchestrator.go:158-164）。</summary>
    public string[] Downstream(string configName)
    {
        var path = ToPath(configName);
        var task = GetTask(path);
        return Map(task._downStream.ToArray(), static t => t._config);
    }

    /// <summary>Go: <c>func (o *Orchestrator) getTask(path tspath.Path) *BuildTask</c>（orchestrator.go:166-172）。</summary>
    internal BuildTask GetTask(Path path)
    {
        var (task, ok) = _tasks.Load(path);
        if (!ok)
        {
            Debug.Fail("No build task found for " + path.Value);
            throw new System.Diagnostics.UnreachableException();
        }
        return task!;
    }

    /// <summary>Go: <c>func (o *Orchestrator) createBuildTasks(oldTasks, configs, wg) </c>（orchestrator.go:174-208）。</summary>
    internal void CreateBuildTasks(SyncMap<Path, BuildTask>? oldTasks, string[] configs, WorkGroup wg)
    {
        foreach (var config in configs)
        {
            var capturedConfig = config; // Go 的闭包捕获（for 变量）
            wg.Queue(() =>
            {
                var path = ToPath(capturedConfig);
                BuildTask? task = null;
                BuildInfoEntry? buildInfo = null;
                if (oldTasks is not null)
                {
                    var (existing, ok) = oldTasks.Load(path);
                    if (ok && !existing!._dirty)
                    {
                        // Reuse existing task if config is same
                        task = existing;
                    }
                    else if (ok)
                    {
                        CloseProjectQuietly(existing!._contentMapperProject);
                        buildInfo = existing._buildInfoEntry;
                    }
                }
                if (task is null)
                {
                    task = new BuildTask { _config = capturedConfig, _isInitialCycle = oldTasks is null };
                    task._pending = true;
                    task._buildInfoEntry = buildInfo;
                }
                if (_tasks.LoadOrStore(path, task).Loaded)
                {
                    return;
                }
                task._resolved = _host.GetResolvedProjectReference(capturedConfig, path);
                task._upStream = [];
                if (task._resolved is not null)
                {
                    CreateBuildTasks(oldTasks, task._resolved.ResolvedProjectReferencePaths(), wg);
                }
            });
        }
    }

    /// <summary>Go: <c>func (o *Orchestrator) setupBuildTask(...) *BuildTask</c>（orchestrator.go:210-250）。</summary>
    internal BuildTask? SetupBuildTask(
        string configName,
        BuildTask? downStream,
        bool inCircularContext,
        Set<Path> completed,
        Set<Path> analyzing,
        List<string> circularityStack)
    {
        var path = ToPath(configName);
        var task = GetTask(path);
        if (!completed.Has(path))
        {
            if (analyzing.Has(path))
            {
                if (!inCircularContext)
                {
                    _errors.Add(NewCompilerDiagnostic(
                        Project_references_may_not_form_a_circular_graph_Cycle_detected_Colon_0,
                        string.Join("\n", circularityStack)));
                }
                return null;
            }
            analyzing.Add(path);
            circularityStack.Add(configName);
            if (task._resolved is not null)
            {
                var subReferences = task._resolved.ResolvedProjectReferencePaths();
                for (var index = 0; index < subReferences.Length; index++)
                {
                    var subReference = subReferences[index];
                    var upstream = SetupBuildTask(
                        subReference,
                        task,
                        inCircularContext || task._resolved.ProjectReferences()![index].Circular,
                        completed,
                        analyzing,
                        circularityStack);
                    if (upstream is not null)
                    {
                        task._upStream.Add(new UpstreamTask { _task = upstream, _refIndex = index });
                    }
                }
            }
            circularityStack.RemoveAt(circularityStack.Count - 1);
            completed.Add(path);
            task._built = new ManualResetEventSlim();
            task._done = new ManualResetEventSlim();
            _order.Add(configName);
        }
        if (_opts.Command.CompilerOptions!.Watch.IsTrue() && downStream is not null)
        {
            task._downStream.Add(downStream);
        }
        return task;
    }

    /// <summary>Go: <c>func (o *Orchestrator) GenerateGraphReusingOldTasks()</c>（orchestrator.go:252-258）。</summary>
    public void GenerateGraphReusingOldTasks()
    {
        var tasks = _tasks;
        _tasks = new SyncMap<Path, BuildTask>();
        _order = [];
        _errors = [];
        GenerateGraph(tasks);
    }

    /// <summary>Go: <c>func (o *Orchestrator) GenerateGraph(oldTasks *collections.SyncMap[tspath.Path, *BuildTask])</c>（orchestrator.go:260-288）。</summary>
    public void GenerateGraph(SyncMap<Path, BuildTask>? oldTasks)
    {
        var projects = _opts.Command.ResolvedProjectPaths();
        // Parse all config files in parallel
        var wg = NewWorkGroup(_opts.Command.CompilerOptions!.SingleThreaded.IsTrue());
        CreateBuildTasks(oldTasks, projects, wg);
        wg.RunAndWait();

        // Generate the graph
        var completed = new Set<Path>();
        var analyzing = new Set<Path>();
        var circularityStack = new List<string>();
        foreach (var project in projects)
        {
            SetupBuildTask(project, null, false, completed, analyzing, circularityStack);
        }
        _scheduleOrder = ComputeScheduleOrder();
        if (oldTasks is not null)
        {
            oldTasks.Range((path, oldTask) =>
            {
                var (task, ok) = _tasks.Load(path);
                if (ok && ReferenceEquals(task, oldTask))
                {
                    return true;
                }
                CloseProjectQuietly(oldTask!._contentMapperProject);
                return true;
            });
        }
        _graphGenerated = true;
    }

    /// <summary>Go: <c>func (o *Orchestrator) Start(ctx context.Context) tsc.CommandLineResult</c>（orchestrator.go:290-293）——`tsc -b` 入口。</summary>
    public TscNs.CommandLineResult Start(Context ctx) => StartWorker(ctx, "", false /*onlyReferences*/).Result;

    /// <summary>Go: <c>func (o *Orchestrator) Build(ctx, project string) *OrchestratorResult</c>（orchestrator.go:295-299）——api 入口。</summary>
    public OrchestratorResult Build(Context ctx, string project)
    {
        RecheckAllProjects(project);
        return StartWorker(ctx, project, false /*onlyReferences*/);
    }

    /// <summary>Go: <c>func (o *Orchestrator) BuildReferences(ctx, project string) *OrchestratorResult</c>（orchestrator.go:301-305）——api 入口。</summary>
    public OrchestratorResult BuildReferences(Context ctx, string project)
    {
        RecheckAllProjects(project);
        return StartWorker(ctx, project, true /*onlyReferences*/);
    }

    /// <summary>Go: <c>func (o *Orchestrator) start(ctx, project string, onlyReferences bool) *OrchestratorResult</c>（orchestrator.go:306-335）。</summary>
    internal OrchestratorResult StartWorker(Context ctx, string project, bool onlyReferences)
    {
        _contentMapperHost = NewContentMapperHost(ctx, _opts.Sys, _opts.Command.CompilerOptions!);
        var closeContentMapperHost = _contentMapperHost is not null &&
            (!_opts.Command.CompilerOptions!.Watch.IsTrue() || _opts.Testing is null);
        try
        {
            if (_opts.Command.CompilerOptions!.Watch.IsTrue())
            {
                _watchStatusReporter!(NewCompilerDiagnostic(Starting_compilation_in_watch_mode));
            }
            if (_graphGenerated)
            {
                GenerateGraphReusingOldTasks();
            }
            else
            {
                GenerateGraph(null);
            }
            var (order, ok) = GetBuildOrderFor(project);
            if (!ok)
            {
                return new OrchestratorResult { Result = new TscNs.CommandLineResult { Status = TscNs.ExitStatus.InvalidProject_OutputsSkipped } };
            }
            if (onlyReferences && _errors.Count == 0)
            {
                if (project == "")
                {
                    return new OrchestratorResult { Result = new TscNs.CommandLineResult { Status = TscNs.ExitStatus.InvalidProject_OutputsSkipped } };
                }
                order = order!.GetRange(0, order.Count - 1);
            }
            var result = BuildOrCleanOrder(order!);
            if (_opts.Command.CompilerOptions!.Watch.IsTrue())
            {
                Watch(ctx);
                result.Result.Watcher = this;
            }
            return result;
        }
        finally
        {
            if (closeContentMapperHost)
            {
                CloseHostQuietly(_contentMapperHost);
            }
        }
    }

    /// <summary>Go: <c>func (o *Orchestrator) recheckAllProjects(project string)</c>（orchestrator.go:337-352）。</summary>
    internal void RecheckAllProjects(string project)
    {
        if (!_graphGenerated)
        {
            return;
        }
        var (order, ok) = GetBuildOrderFor(project);
        if (!ok)
        {
            return;
        }
        RangeTasks(order!, (path, task) =>
        {
            task.ResetStatus();
            task.ResetConfig(this, ToPath(task._config));
        });
        _host._mTimes = new SyncMap<Path, DateTime>();
        ResetCaches();
    }

    /// <summary>Go: <c>func (o *Orchestrator) Clean(project string) *OrchestratorResult</c>（orchestrator.go:354-357）——api 入口。</summary>
    public OrchestratorResult Clean(string project) => CleanWorker(project, false);

    /// <summary>Go: <c>func (o *Orchestrator) CleanReferences(project string) *OrchestratorResult</c>（orchestrator.go:359-361）——api 入口。</summary>
    public OrchestratorResult CleanReferences(string project) => CleanWorker(project, true);

    /// <summary>Go: <c>func (o *Orchestrator) clean(project string, onlyReferences bool) *OrchestratorResult</c>（orchestrator.go:363-414）。</summary>
    internal OrchestratorResult CleanWorker(string project, bool onlyReferences)
    {
        if (!_graphGenerated)
        {
            GenerateGraph(null);
        }
        if (_errors.Count != 0)
        {
            var cycleResult = new OrchestratorResult
            {
                Result = new TscNs.CommandLineResult { Status = TscNs.ExitStatus.ProjectReferenceCycle_OutputsSkipped },
                Errors = _errors,
            };
            cycleResult.ReportWithFilesToDelete(this, true);
            return cycleResult;
        }

        var (order, ok) = GetBuildOrderFor(project);
        if (!ok)
        {
            return new OrchestratorResult { Result = new TscNs.CommandLineResult { Status = TscNs.ExitStatus.InvalidProject_OutputsSkipped } };
        }
        if (onlyReferences)
        {
            order = order!.GetRange(0, order.Count - 1);
        }

        var result = new OrchestratorResult();
        result.Statistics.Projects = order!.Count;
        var dry = _opts.Command.BuildOptions!.Dry.IsTrue();
        var reportDiagnostic = CreateDiagnosticReporter(null);
        foreach (var config in order)
        {
            var task = GetTask(ToPath(config));
            if (task._resolved is null)
            {
                var diagnostic = NewCompilerDiagnostic(File_0_not_found, task._config);
                reportDiagnostic(diagnostic);
                result.Errors.Add(diagnostic);
                continue;
            }

            var inputs = NewSetFromItems(Map(task._resolved.FileNames(), ToPath));
            var projectOutputs = task._resolved.GetOutputFileNames();
            var deleted = false;
            foreach (var outputFile in projectOutputs)
            {
                deleted = CleanProjectOutput(outputFile, inputs, dry, result.FilesToDelete, reportDiagnostic) || deleted;
            }
            deleted = CleanProjectOutput(task._resolved.GetBuildInfoFileName(), inputs, dry, result.FilesToDelete, reportDiagnostic) || deleted;
            if (deleted)
            {
                task.ResetStatus();
                lock (task._buildInfoEntryMu)
                {
                    task._buildInfoEntry = null;
                }
            }
        }

        result.ReportWithFilesToDelete(this, dry);
        return result;
    }

    /// <summary>Go: <c>func (o *Orchestrator) getBuildOrderFor(project string) ([]string, bool)</c>（orchestrator.go:416-450）。</summary>
    internal (List<string>? Order, bool Ok) GetBuildOrderFor(string project)
    {
        if (project == "")
        {
            return (_order, true);
        }

        var config = ResolveConfigFileNameOfProjectReference(
            TPath.ResolvePath(_opts.Sys.GetCurrentDirectory(), project));
        var (target, found) = _tasks.Load(ToPath(config));
        if (!found)
        {
            return (null, false);
        }

        var projects = new Set<Path>();
        void AddProjectAndReferences(BuildTask task)
        {
            var path = ToPath(task._config);
            if (projects.Has(path))
            {
                return;
            }
            projects.Add(path);
            foreach (var upstream in task._upStream)
            {
                AddProjectAndReferences(upstream._task);
            }
        }
        AddProjectAndReferences(target!);

        var order = new List<string>(projects.M.Count);
        foreach (var configName in _order)
        {
            if (projects.Has(ToPath(configName)))
            {
                order.Add(configName);
            }
        }
        return (order, true);
    }

    /// <summary>Go: <c>func (o *Orchestrator) cleanProjectOutput(...) bool</c>（orchestrator.go:452-471）。</summary>
    internal bool CleanProjectOutput(
        string outputFile,
        Set<Path> inputs,
        bool dry,
        List<string> filesToDelete,
        TscNs.DiagnosticReporter reportDiagnostic)
    {
        if (outputFile == "" || inputs.Has(ToPath(outputFile)) || !_host.FS().FileExists(outputFile))
        {
            return false;
        }
        filesToDelete.Add(outputFile);
        if (dry)
        {
            return false;
        }
        try
        {
            _host.FS().Remove(outputFile);
        }
        catch (Exception)
        {
            reportDiagnostic(NewCompilerDiagnostic(Failed_to_delete_file_0, outputFile));
            return false;
        }
        return true;
    }

    /// <summary>Go: <c>func (o *Orchestrator) Watch(ctx context.Context)</c>（orchestrator.go:473-496）。</summary>
    public void Watch(Context ctx)
    {
        _wm.Lock();
        try
        {
            if (_opts.Testing is null)
            {
                if (_opts.Sys.TryGetEnvironmentVariable("TS_WATCH_DEBUG", out var value) && value != "")
                {
                    _wm.DebugLog = _opts.Sys.Writer();
                }
                _wm.EnsureDefaultBackend();
            }

            UpdateWatch();
            var desiredDirs = ComputeDesiredWatches();
            try
            {
                _wm.ReconcileWatches(desiredDirs);
            }
            catch (Exception err)
            {
                _opts.Sys.Writer().Write(err.Message + "\n");
                _wm.ForceOverflow();
            }
            ResetCaches();
        }
        finally
        {
            _wm.Unlock();
        }

        if (_opts.Testing is null)
        {
            _wm.RunLoop(ctx, DoCycle);
        }
    }

    /// <summary>Go: <c>func (o *Orchestrator) updateWatch()</c>（orchestrator.go:498-504）。</summary>
    internal void UpdateWatch()
    {
        var oldCache = _host._mTimes;
        _host._mTimes = new SyncMap<Path, DateTime>();
        RangeTask((path, task) => task.UpdateWatch(this, oldCache));
    }

    /// <summary>Go: <c>func (o *Orchestrator) resetCaches()</c>（orchestrator.go:506-513）。</summary>
    internal void ResetCaches()
    {
        // Clean out all the caches
        var cachesVfs = (CachedVfs.FS)_host._host.FS();
        cachesVfs.ClearCache();
        _host._extendedConfigCache = new TscNs.ExtendedConfigCache();
        _host._sourceFiles.Reset();
        _host._configTimes = new SyncMap<Path, TimeSpan>();
    }

    /// <summary>Go: <c>func (o *Orchestrator) checkTasksForEventChanges(changedPaths, needsConfigUpdate, needsUpdate)</c>（orchestrator.go:515-671）。</summary>
    internal void CheckTasksForEventChanges(Dictionary<string, EventKind> changedPaths, ref bool needsConfigUpdate, ref bool needsUpdate)
    {
        var normalizedPaths = new Dictionary<Path, EventKind>(changedPaths.Count);
        foreach (var (eventPath, kind) in changedPaths)
        {
            normalizedPaths[ToPath(eventPath)] = kind;
        }

        foreach (var config in _order)
        {
            var path = ToPath(config);
            var task = GetTask(path);

            var configPath = ToPath(task._config);
            if (normalizedPaths.ContainsKey(configPath))
            {
                task.ResetConfig(this, path);
                needsConfigUpdate = true;
                needsUpdate = true;
                continue;
            }

            if (task._resolved is null)
            {
                continue;
            }

            var configChanged = false;
            foreach (var file in task._resolved.ExtendedSourceFiles() ?? [])
            {
                var fp = ToPath(file);
                if (normalizedPaths.ContainsKey(fp))
                {
                    task.ResetConfig(this, path);
                    needsConfigUpdate = true;
                    needsUpdate = true;
                    configChanged = true;
                    break;
                }
            }
            if (configChanged)
            {
                continue;
            }
            foreach (var mapper in task._resolved.ContentMappers() ?? [])
            {
                if (mapper.PackageDirectory == "" || mapper.ContributionID != "")
                {
                    continue;
                }
                var manifestPath = ToPath(CombinePaths(mapper.PackageDirectory, "package.json"));
                if (normalizedPaths.ContainsKey(manifestPath))
                {
                    task.ResetConfig(this, path);
                    needsConfigUpdate = true;
                    needsUpdate = true;
                    configChanged = true;
                    break;
                }
            }
            if (configChanged)
            {
                continue;
            }

            var rootChanged = false;
            if (task._contentMapperProject is not null)
            {
                string[] watchedFiles;
                try
                {
                    watchedFiles = task._contentMapperProject.WatchedFiles();
                }
                catch (Exception err)
                {
                    task._contentMapperProjectErr = err;
                    task.ResetStatus();
                    needsUpdate = true;
                    rootChanged = true;
                    watchedFiles = [];
                }
                foreach (var fileName in watchedFiles)
                {
                    if (normalizedPaths.ContainsKey(ToPath(fileName)))
                    {
                        task.RefreshContentMapperProject(this);
                        task.ResetStatus();
                        needsUpdate = true;
                        rootChanged = true;
                        break;
                    }
                }
            }
            var fileNames = task._resolved.FileNames();
            var roots = NewSetWithSizeHint<Path>(fileNames.Length);
            foreach (var file in fileNames)
            {
                var fp = ToPath(file);
                roots.Add(fp);
                if (!rootChanged && normalizedPaths.ContainsKey(fp))
                {
                    task.ResetStatus();
                    needsUpdate = true;
                    rootChanged = true;
                }
            }

            if (!rootChanged)
            {
                BuildInfoEntry? bi;
                lock (task._buildInfoEntryMu)
                {
                    bi = task._buildInfoEntry;
                }
                if (bi is not null && bi._buildInfo is not null)
                {
                    var buildInfoDir = GetDirectoryPath(bi._path.Value);
                    foreach (var fileName in bi._buildInfo.FileNames ?? [])
                    {
                        var fp = ToPath(ResolveBuildInfoFileName(fileName, buildInfoDir));
                        if (roots.Has(fp))
                        {
                            continue;
                        }
                        if (normalizedPaths.ContainsKey(fp))
                        {
                            task.ResetStatus();
                            needsUpdate = true;
                            break;
                        }
                    }
                    foreach (var packageJson in bi._buildInfo.GetPackageJsons(buildInfoDir))
                    {
                        if (PackageJsonLookupChanged(packageJson, normalizedPaths))
                        {
                            task.ResetStatus();
                            needsUpdate = true;
                            break;
                        }
                    }
                    foreach (var packageJson in bi._buildInfo.GetMissingPackageJsons(buildInfoDir))
                    {
                        if (PackageJsonLookupChanged(packageJson, normalizedPaths))
                        {
                            task.ResetStatus();
                            needsUpdate = true;
                            break;
                        }
                    }
                }
                foreach (var packageJson in task._packageJsons)
                {
                    if (PackageJsonLookupChanged(packageJson, normalizedPaths))
                    {
                        task.ResetStatus();
                        needsUpdate = true;
                        break;
                    }
                }
            }

            task._built = new ManualResetEventSlim();
            task._done = new ManualResetEventSlim();

            var newConfig = task._resolved.ReloadFileNamesOfParsedCommandLine(_host.FS());
            if (!Enumerable.SequenceEqual(task._resolved.FileNames(), newConfig.FileNames()))
            {
                _host._resolvedReferences.Store(path, newConfig);
                task._resolved = newConfig;
                task.ResetStatus();
                needsUpdate = true;
            }
        }

        if (!needsUpdate)
        {
            var opts = _comparePathsOptions;
            foreach (var eventPath in changedPaths.Keys)
            {
                if (_host.FS().DirectoryExists(eventPath) && _wm.IsPathUnderWatch(eventPath, opts))
                {
                    RangeTask((path, task) =>
                    {
                        task.ResetStatus();
                        task._built = new ManualResetEventSlim();
                        task._done = new ManualResetEventSlim();
                    });
                    needsUpdate = true;
                    break;
                }
            }
        }
    }

    /// <summary>Go: <c>func (o *Orchestrator) packageJsonLookupChanged(packageJson string, changedPaths) bool</c>（orchestrator.go:673-684）。</summary>
    internal bool PackageJsonLookupChanged(string packageJson, Dictionary<Path, EventKind> changedPaths)
    {
        var packageJsonPath = ToPath(packageJson);
        if (changedPaths.ContainsKey(packageJsonPath))
        {
            return true;
        }
        foreach (var (changedPath, kind) in changedPaths)
        {
            if (kind == EventKind.Delete && ContainsPath(changedPath.Value, packageJsonPath.Value, _comparePathsOptions))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Go: <c>func (o *Orchestrator) computeDesiredWatches() map[string]bool</c>（orchestrator.go:686-779）。</summary>
    internal Dictionary<string, bool> ComputeDesiredWatches()
    {
        var desiredDirs = NewDirWatchSet(_comparePathsOptions);

        foreach (var config in _order)
        {
            var path = ToPath(config);
            var task = GetTask(path);

            // Watch config file directory
            var configDir = GetDirectoryPath(task._config);
            var realConfigDir = _host.FS().Realpath(configDir);
            desiredDirs.Set(realConfigDir, false);

            if (task._resolved is null)
            {
                continue;
            }

            // Extended config file directories
            foreach (var cfgPath in task._resolved.ExtendedSourceFiles() ?? [])
            {
                var realPath = _host.FS().Realpath(cfgPath);
                var dir = GetDirectoryPath(realPath);
                desiredDirs.Set(dir, false);
            }

            // Wildcard directories from tsconfig
            foreach (var (dir, recursive) in task._resolved.WildcardDirectories() ?? [])
            {
                var realDir = _host.FS().Realpath(dir);
                desiredDirs.Set(realDir, recursive);
            }

            // Input file directories not already covered
            foreach (var fileName in task._resolved.FileNames())
            {
                var absPath = GetNormalizedAbsolutePath(fileName, _opts.Sys.GetCurrentDirectory());
                var dir = GetDirectoryPath(absPath);
                if (!desiredDirs.Covered(dir) && CanWatchDirectory(dir))
                {
                    desiredDirs.Set(dir, false);
                }
                foreach (var mapper in task._resolved.ContentMappers() ?? [])
                {
                    if (mapper.PackageDirectory == "" || mapper.ContributionID != "")
                    {
                        continue;
                    }
                    var manifestPath = CombinePaths(mapper.PackageDirectory, "package.json");
                    var mapperDir = GetDirectoryPath(manifestPath);
                    if (!desiredDirs.Covered(mapperDir) && CanWatchDirectory(mapperDir))
                    {
                        desiredDirs.Set(mapperDir, false);
                    }
                }
            }
            if (task._contentMapperProject is not null)
            {
                string[] watchedFiles;
                try
                {
                    watchedFiles = task._contentMapperProject.WatchedFiles();
                }
                catch (Exception err)
                {
                    task._contentMapperProjectErr = err;
                    watchedFiles = [];
                }
                foreach (var fileName in watchedFiles)
                {
                    var absPath = _host.FS().Realpath(fileName);
                    var dir = GetDirectoryPath(absPath);
                    if (!desiredDirs.Covered(dir) && CanWatchDirectory(dir))
                    {
                        desiredDirs.Set(dir, false);
                    }
                }
            }

            // Non-root dependency directories from buildinfo (e.g. node_modules .d.ts files).
            BuildInfoEntry? bi;
            lock (task._buildInfoEntryMu)
            {
                bi = task._buildInfoEntry;
            }
            if (bi is not null && bi._buildInfo is not null)
            {
                var buildInfoDir = GetDirectoryPath(bi._path.Value);
                var roots = NewSetFromItems(Map(task._resolved.FileNames(), ToPath));
                foreach (var fileName in bi._buildInfo.FileNames ?? [])
                {
                    var absPath = _host.FS().Realpath(ResolveBuildInfoFileName(fileName, buildInfoDir));
                    var fp = ToPath(absPath);
                    if (roots.Has(fp))
                    {
                        continue;
                    }
                    var dir = GetDirectoryPath(absPath);
                    if (!desiredDirs.Covered(dir) && CanWatchDirectory(dir))
                    {
                        desiredDirs.Set(dir, false);
                    }
                }
                foreach (var packageJson in bi._buildInfo.GetPackageJsons(buildInfoDir))
                {
                    AddPackageJsonWatchDirs(desiredDirs, packageJson);
                }
                foreach (var packageJson in bi._buildInfo.GetMissingPackageJsons(buildInfoDir))
                {
                    AddPackageJsonWatchDirs(desiredDirs, packageJson);
                }
            }
            foreach (var packageJson in task._packageJsons)
            {
                AddPackageJsonWatchDirs(desiredDirs, packageJson);
            }
        }

        return _wm.ResolveDesiredDirs(desiredDirs.Dirs());
    }

    /// <summary>Go: <c>func (o *Orchestrator) addWatchDir(desiredDirs *watchmanager.DirWatchSet, dir string)</c>（orchestrator.go:781-785）。</summary>
    internal void AddWatchDir(DirWatchSet desiredDirs, string dir)
    {
        if (!desiredDirs.Covered(dir) && CanWatchDirectory(dir))
        {
            desiredDirs.Set(dir, false);
        }
    }

    /// <summary>Go: <c>func (o *Orchestrator) addPackageJsonWatchDirs(desiredDirs *watchmanager.DirWatchSet, packageJson string)</c>（orchestrator.go:787-814）。</summary>
    internal void AddPackageJsonWatchDirs(DirWatchSet desiredDirs, string packageJson)
    {
        var dir = GetDirectoryPath(packageJson);
        var dirs = new List<string> { dir };
        var foundNodeModules = false;
        var current = dir;
        while (true)
        {
            var parent = GetDirectoryPath(current);
            if (parent == "" || parent == current)
            {
                break;
            }
            dirs.Add(parent);
            if (GetBaseFileName(parent) == "node_modules")
            {
                foundNodeModules = true;
                var grandparent = GetDirectoryPath(parent);
                if (grandparent != "" && grandparent != parent)
                {
                    dirs.Add(grandparent);
                }
                break;
            }
            current = parent;
        }

        if (!foundNodeModules)
        {
            AddWatchDir(desiredDirs, dir);
            return;
        }
        foreach (var watchDir in dirs)
        {
            AddWatchDir(desiredDirs, watchDir);
        }
    }

    /// <summary>Go: <c>func (o *Orchestrator) DoCycle()</c>（orchestrator.go:816-867）。</summary>
    public void DoCycle()
    {
        _wm.Lock();
        try
        {
            _wm.DrainEvents(out var changedPaths, out var overflow);
            var hasEvents = (changedPaths is not null && changedPaths.Count > 0) || overflow;

            if (!hasEvents)
            {
                if (_wm.DebugLog is not null)
                {
                    _wm.DebugLog.Write("[watch] DoCycle: no events, skipping\n");
                }
                return;
            }

            var needsConfigUpdate = false;
            var needsUpdate = false;

            if (overflow)
            {
                // Overflow: reset all tasks to force a full rebuild.
                RangeTask((path, task) =>
                {
                    task.ResetConfig(this, path);
                    task._built = new ManualResetEventSlim();
                    task._done = new ManualResetEventSlim();
                });
                needsConfigUpdate = true;
                needsUpdate = true;
            }
            else
            {
                // Event-driven: check only tasks affected by changed paths
                CheckTasksForEventChanges(changedPaths!, ref needsConfigUpdate, ref needsUpdate);
            }

            if (!needsUpdate)
            {
                ResetCaches();
                return;
            }

            _watchStatusReporter!(NewCompilerDiagnostic(File_change_detected_Starting_incremental_compilation));
            if (needsConfigUpdate)
            {
                // Generate new tasks
                GenerateGraphReusingOldTasks();
            }

            BuildOrClean();
            UpdateWatch();
            var desiredDirs = ComputeDesiredWatches();
            try
            {
                _wm.ReconcileWatches(desiredDirs);
            }
            catch (Exception err)
            {
                _opts.Sys.Writer().Write(err.Message + "\n");
                // Mark overflow so the next event triggers a full rebuild
                _wm.ForceOverflow();
            }
            ResetCaches();
        }
        finally
        {
            _wm.Unlock();
        }
    }

    /// <summary>Go: <c>func (o *Orchestrator) buildOrClean() tsc.CommandLineResult</c>（orchestrator.go:869-871）。</summary>
    internal TscNs.CommandLineResult BuildOrClean() => BuildOrCleanOrder(_order).Result;

    /// <summary>Go: <c>func (o *Orchestrator) buildOrCleanOrder(order []string) *OrchestratorResult</c>（orchestrator.go:873-917）。</summary>
    internal OrchestratorResult BuildOrCleanOrder(List<string> order)
    {
        if (!_opts.Command.BuildOptions!.Clean.IsTrue() && _opts.Command.BuildOptions.Verbose.IsTrue())
        {
            CreateBuilderStatusReporter(null)(NewCompilerDiagnostic(
                Projects_in_this_build_Colon_0,
                string.Join("", Map(order.ToArray(), p => "\r\n    * " + RelativeFileName(p)))));
        }
        var buildResult = new OrchestratorResult();
        if (_errors.Count == 0)
        {
            buildResult.Statistics.Projects = order.Count;
            // Builders pick up projects in scheduleOrder; results are reported in Order(), waiting for each project to finish
            var reported = new ManualResetEventSlim();
            Task.Run(() =>
            {
                try
                {
                    foreach (var config in order)
                    {
                        var path = ToPath(config);
                        var task = GetTask(path);
                        task._built.Wait();
                        task.Report(this, path, buildResult);
                    }
                }
                finally
                {
                    reported.Set();
                }
            });
            RangeTasks(order, (path, task) => BuildOrCleanProject(task, path));
            reported.Wait();
        }
        else
        {
            // Circularity errors prevent any project from being built
            buildResult.Result.Status = TscNs.ExitStatus.ProjectReferenceCycle_OutputsSkipped;
            var reportDiagnostic = CreateDiagnosticReporter(null);
            foreach (var err in _errors)
            {
                reportDiagnostic(err);
            }
            buildResult.Errors = _errors;
        }
        buildResult.Report(this);
        return buildResult;
    }

    /// <summary>Go: <c>func (o *Orchestrator) rangeTask(f func(path, task))</c>（orchestrator.go:919-921）。</summary>
    internal void RangeTask(Action<Path, BuildTask> f) => RangeTasks(_order, f);

    /// <summary>
    /// Go: <c>func (o *Orchestrator) rangeTasks(order []string, f func(path, task))</c>（orchestrator.go:923-957）。
    /// 见文件头偏离 3：并发运行时用显式大栈线程（§3.9），而不是 Go 的 `core.NewWorkGroup(false)`。
    /// </summary>
    internal void RangeTasks(List<string> order, Action<Path, BuildTask> f)
    {
        var numRoutines = 4;
        if (_opts.Command.CompilerOptions!.SingleThreaded.IsTrue())
        {
            numRoutines = 1;
        }
        else if (_opts.Command.BuildOptions!.Builders is { } builders)
        {
            numRoutines = builders;
        }

        // Go: var currentTaskIndex atomic.Int64 —— 每个 worker 原子取下一个下标。
        var currentTaskIndex = 0;
        (Path Path, BuildTask Task, bool Ok) GetNextTask()
        {
            var index = Interlocked.Increment(ref currentTaskIndex) - 1;
            if (index >= order.Count)
            {
                return (default, null!, false);
            }
            var config = order[index];
            var path = ToPath(config);
            return (path, GetTask(path), true);
        }
        void RunTask()
        {
            for (var (path, task, ok) = GetNextTask(); ok; (path, task, ok) = GetNextTask())
            {
                f(path, task);
            }
        }

        if (numRoutines == 1)
        {
            RunTask();
            return;
        }

        ExceptionDispatchInfo? captured = null;
        var threads = new Thread[numRoutines];
        for (var i = 0; i < numRoutines; i++)
        {
            threads[i] = new Thread(() =>
            {
                try
                {
                    RunTask();
                }
                catch (Exception ex)
                {
                    // Go 里 queued 函数 panic 直接终止进程；这里记录第一个异常并在 join 后重抛（与 Core.WorkGroup 一致）。
                    Interlocked.CompareExchange(ref captured, ExceptionDispatchInfo.Capture(ex), null);
                }
            }, _stackSize);
        }
        foreach (var thread in threads)
        {
            thread.Start();
        }
        foreach (var thread in threads)
        {
            thread.Join();
        }
        captured?.Throw();
    }

    /// <summary>Go: <c>func (o *Orchestrator) buildOrCleanProject(task *BuildTask, path tspath.Path)</c>（orchestrator.go:959-974）。</summary>
    internal void BuildOrCleanProject(BuildTask task, Path path)
    {
        task._result = new TaskResult();
        task._result._reportStatus = CreateBuilderStatusReporter(task);
        task._result._diagnosticReporter = CreateDiagnosticReporter(task);
        if (!_opts.Command.BuildOptions!.Clean.IsTrue())
        {
            task.BuildProject(this, path);
        }
        else
        {
            task.CleanProject(this, path);
        }
        if (_opts.Testing is null)
        {
            // The program is only needed by Testing.OnProgram at report time; drop it now so a task
            // that has finished but is not yet reported does not keep its program alive.
            task._result._program = null;
        }
        task._built.Set();
    }

    /// <summary>Go: <c>func (o *Orchestrator) getWriter(task *BuildTask) io.Writer</c>（orchestrator.go:976-981）。</summary>
    internal TextWriter GetWriter(BuildTask? task) => task is null ? _opts.Sys.Writer() : task._result!._builder;

    /// <summary>Go: <c>func (o *Orchestrator) createBuilderStatusReporter(task *BuildTask) tsc.DiagnosticReporter</c>（orchestrator.go:983-985）。</summary>
    internal TscNs.DiagnosticReporter CreateBuilderStatusReporter(BuildTask? task) =>
        TscNs.Tsc.CreateBuilderStatusReporter(_opts.Sys, GetWriter(task), _opts.Command.Locale, _opts.Command.CompilerOptions, _opts.Testing);

    /// <summary>Go: <c>func (o *Orchestrator) createDiagnosticReporter(task *BuildTask) tsc.DiagnosticReporter</c>（orchestrator.go:987-989）。</summary>
    internal TscNs.DiagnosticReporter CreateDiagnosticReporter(BuildTask? task) =>
        TscNs.Tsc.CreateDiagnosticReporter(_opts.Sys, GetWriter(task), _opts.Command.Locale, _opts.Command.CompilerOptions);

    /// <summary>Go 的 `chan struct{}` 关闭/等待在本文件用 <see cref="ManualResetEventSlim"/> 表达（见 BuildTask.cs）。</summary>
    private const int _stackSize = 64 * 1024 * 1024;

    /// <summary>Go 里 `_ = project.Close()` 丢弃 error 的调用点（§3.2 的 error → 异常，这里照 Go 丢弃）。</summary>
    internal static void CloseProjectQuietly(CmProject? project)
    {
        if (project is null)
        {
            return;
        }
        try
        {
            project.Close();
        }
        catch (Exception)
        {
            // Go: _ = project.Close()（丢弃错误）
        }
    }

    /// <summary>Go 的 `defer o.contentMapperHost.Close()`（orchestrator.go:307-309；错误同样被丢弃）。</summary>
    internal static void CloseHostQuietly(ContentMapper.Host? host)
    {
        if (host is null)
        {
            return;
        }
        try
        {
            host.Close();
        }
        catch (Exception)
        {
            // Go: defer o.contentMapperHost.Close()（丢弃错误）
        }
    }
}

/// <summary>Go 的包级函数（无接收者）落在与包同名的静态类里（docs/architecture.md §2）。</summary>
public static partial class Build
{
    /// <summary>Go: <c>func NewOrchestrator(opts Options) *Orchestrator</c>（orchestrator.go:991-1023）。</summary>
    public static Orchestrator NewOrchestrator(Options opts)
    {
        var wm = NewWatchManager(opts.Sys.Writer(), opts.Sys.FS().DirectoryExists);
        var orchestrator = new Orchestrator
        {
            _opts = opts,
            _comparePathsOptions = new ComparePathsOptions(opts.Sys.FS().UseCaseSensitiveFileNames(), opts.Sys.GetCurrentDirectory()),
            _tasks = new SyncMap<Path, BuildTask>(),
            _wm = wm,
        };
        orchestrator._host = new Host
        {
            _orchestrator = orchestrator,
            _host = TypeScript.Compiler.Compiler.NewCachedFSCompilerHost(
                orchestrator._opts.Sys.GetCurrentDirectory(),
                orchestrator._opts.Sys.FS(),
                orchestrator._opts.Sys.DefaultLibraryPath(),
                null,
                null,
                null),
            _mTimes = new SyncMap<Path, DateTime>(),
        };
        if (opts.Command.CompilerOptions!.Watch.IsTrue())
        {
            orchestrator._watchStatusReporter = CreateWatchStatusReporter(opts.Sys, opts.Command.Locale, opts.Command.CompilerOptions, opts.Testing);
            if (opts.Testing is CommandLineTestingWithWatchBackend testingWithBackend)
            {
                wm.SetBackend(testingWithBackend.WatchBackend());
            }
        }
        else
        {
            orchestrator._errorSummaryReporter = CreateReportErrorSummary(opts.Sys, opts.Command.Locale, opts.Command.CompilerOptions);
        }
        return orchestrator;
    }
}
