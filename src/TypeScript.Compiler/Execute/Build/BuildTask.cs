// ported from: internal/execute/build/buildtask.go
//
// 与 Go 的偏离（docs/architecture.md §2/§3）：
// 1) 未导出类型 → C# 习惯命名（§2）：`buildKind` → `BuildKind`、`upstreamTask` → `UpstreamTask`、
//    `buildInfoEntry` → `BuildInfoEntry`、`taskResult` → `TaskResult`；枚举成员名按 §3.3 去前缀
//    （`buildKindNone/Pseudo/Program` → `None/Pseudo/Program`，底层类型照 Go 的 `uint`，数值照 iota）。
//    未导出字段 → `_camelCase`、未导出方法 → internal + PascalCase。Go 导出类型（`BuildTask`）保持 public，
//    但它自己的成员在 Go 里全是未导出成员 → 一律 internal（公开 API 与 TSGO 一致，§2）。
// 2) `chan struct{}`（`done`/`built`）→ `ManualResetEventSlim`：`make(chan struct{})` → `new()`、
//    `close(ch)` → `Set()`、`<-ch` → `Wait()`。本文件只使用 `done`（`built` 由 Orchestrator 负责 Set/Wait，
//    这里只保留同名字段）。
// 3) `atomic.Bool` → `volatile bool`：`Store(b)` → `_pending = b`、`Load()` → `_pending`（§3.2）。
// 4) `sync.Mutex` → `private readonly object _xMu = new()` + `lock`；`sync.Once`（`contentMapperProjectOnce`）→
//    `bool _contentMapperProjectOnceDone` + 一把锁（为此多出私有字段 `_contentMapperProjectOnceMu`，不在公开 API 里）。
//    Go 的 Once 在回调 panic 时同样把 done 置位，这里的顺序与之一致：先置 done 再执行回调体。
// 5) `(T, error)` → 成功返回值 + 失败抛异常（§3.2）。唯一保留 error 槽位的是
//    `GetContentMapperProject` → `(ContentMapper.Project? Project, Exception? Error)`（Go 里它就是把 Once 闭包捕获的
//    error 原样带出，这个槽位本身就是 API 的一部分）。`contentMapperProject.Refresh()` 在 C# 无错误通道 →
//    `RefreshContentMapperProject` 用 try/catch 写 `_contentMapperProjectErr`，并在成功时按 Go 的行为把它清空
//    （Go: `t.contentMapperProjectErr = t.contentMapperProject.Refresh()`）。
//    **例外**：`UpdateTimeStamps` 里 Go **忽略** `host.SetMTime` 的错误（`if err == nil { 记账 }`），而 C# 的
//    `Host.SetMTime` 失败抛异常 → 用 try/catch 吞掉异常，保持"失败就跳过记账"的可观察行为（含注释）。
//    同理 `FS().Remove(...)` 的 error → try/catch 后报 `Failed_to_delete_file_0`。
// 6) `io.Writer` → `TextWriter`；`strings.Builder` 字段 → `StringWriter`，调用点写
//    `w.Write(builder.ToString())`（对应 Go 的 `fmt.Fprint(w, builder.String())`）。
// 7) `iter.Seq[tspath.Path]` → `IEnumerable<Path>`（`Roots()`）；`for x := range seq` → `foreach (var x in seq)`；
//    `slices.Collect(seq)` → 集合表达式 `[.. seq]`（`iter.Seq[string]` 在 C# 侧本来就是 `IEnumerable<string>`）。
// 8) 包级函数没有对应物（§2）→ `BuildTask` 上的 `internal static bool IsContentMapperSupplementalBuildInfoPath(Path,
//    IEnumerable<Path>)`。`strings.CutPrefix`/`strings.Cut` → `StartsWith(StringComparison.Ordinal)` + `AsSpan` 切片
//    （Go 的 CutPrefix 是字节比较；这里的分隔符都是 ASCII，两种下标等价）；`strconv.Atoi` → `int.TryParse`。
// 9) `time.Time` → `DateTime`：`IsZero()` → `== default`、`After/Before` → `>`/`<`（Execute/Incremental/Host.cs 的约定）；
//    `*time.Time` → `DateTime?`；`time.Duration` → `TimeSpan`。
// 10) C# 的 enum 没有 `>` 运算符 → `t.result.exitStatus > buildResult.Result.Status` 写成 `(int)… > (int)…`。
// 11) 类型断言 `data.(string)` / `data.BuildInfo.(*incremental.BuildInfo)` → C# 转换（断言失败对应 Go 的 panic：
//    抛 InvalidCastException）；`panic(fmt.Sprintf(...))` → `Debug.Fail(...)`。
// 12) Go 的 `t.resolved.CompilerOptions()`（`*core.CompilerOptions`）在 C# 返回 `CompilerOptions?` → 照 Go 的非 nil
//    前提写 `!`；同理 `Command.BuildOptions`（`BuildOptions?`）/`Command.CompilerOptions`（`CompilerOptions?`）、
//    `t.result`/`t.status`（Go 在这些点会因 nil 接收者 panic）。
// 13) 与命名空间/类型同名的简单名解析（§2）用别名：`Tsc` = 命名空间 `TypeScript.Execute.Tsc`（用于 `Tsc.ExitStatus`/
//    `Tsc.Statistics`/`Tsc.EmitInput`/`Tsc.CompileTimes`/`Tsc.DiagnosticReporter`），静态类成员另用
//    `using static TypeScript.Execute.Tsc.Tsc;` 后裸名调用（`EmitAndReportStatistics`/`GetTraceWithWriterFromSys`/
//    `QuietDiagnosticsReporter`）；`Inc` = 命名空间 `TypeScript.Execute.Incremental`（`Inc.Program`/`Inc.BuildInfo`…）+
//    `using static TypeScript.Execute.Incremental.Incremental;`（`ComputeHash`/`ContentMapperIdentities`/
//    `IsBuildInfoFileNameDefaultLibrary`/`NewBuildInfoReader`/`ReadBuildInfoProgram`）；`CM` = `TypeScript.ContentMapper`
//    （类型 `CM.Project`/`CM.ProjectSpec`/`CM.Host`）并 `using static TypeScript.ContentMapper.ContentMapper;`
//    （`IsSupportedVirtualExtension` 是包级函数，静态类成员不能用命名空间别名限定）；`Comp` = 静态类
//    `TypeScript.Compiler.Compiler`（写成 `Compiler.NewProgram` 会先解析到命名空间，故按 Execute/Tsc/Emit.cs 的先例用别名）；
//    `Path` = `TypeScript.TSPath.Path`（与 System.IO.Path 冲突）。另外 `NewCompilerDiagnostic` 是包级函数，落在
//    `using static TypeScript.Ast.Ast;`（**不在** class `Diagnostic` 上），`incremental.NewProgram` 是
//    `Inc.Program` 上的静态工厂（与 Execute/Tsc.cs 的 `IncProgram.NewProgram` 同一落点）。
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.TSPath;
using TypeScript.TsOptions;
using CM = TypeScript.ContentMapper;
using Comp = TypeScript.Compiler.Compiler;
using Inc = TypeScript.Execute.Incremental;
using Path = TypeScript.TSPath.Path;
using Tsc = TypeScript.Execute.Tsc;
using static TypeScript.Ast.Ast;
using static TypeScript.Collections.Collections;
using static TypeScript.ContentMapper.ContentMapper;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.Execute.Incremental.Incremental;
using static TypeScript.Execute.Tsc.Tsc;
using static TypeScript.TSPath.TSPath;

namespace TypeScript.Execute.Build;

/// <summary>Go: <c>type buildKind uint</c>（buildtask.go:25）。成员名按 §3.3 去前缀；数值照 iota。</summary>
internal enum BuildKind : uint
{
    /// <summary>Go: <c>buildKindNone</c>。</summary>
    None = 0,

    /// <summary>Go: <c>buildKindPseudo</c>。</summary>
    Pseudo = 1,

    /// <summary>Go: <c>buildKindProgram</c>。</summary>
    Program = 2,
}

/// <summary>Go: <c>type upstreamTask struct</c>（buildtask.go:33-36）。</summary>
internal sealed class UpstreamTask
{
    /// <summary>Go: <c>task *BuildTask</c>。</summary>
    internal BuildTask _task = null!;

    /// <summary>Go: <c>refIndex int</c>。</summary>
    internal int _refIndex;
}

/// <summary>Go: <c>type buildInfoEntry struct</c>（buildtask.go:37-42）。</summary>
internal sealed class BuildInfoEntry
{
    /// <summary>Go: <c>buildInfo *incremental.BuildInfo</c>。</summary>
    internal Inc.BuildInfo? _buildInfo;

    /// <summary>Go: <c>path tspath.Path</c>。</summary>
    internal Path _path;

    /// <summary>Go: <c>mTime time.Time</c>。</summary>
    internal DateTime _mTime;

    /// <summary>Go: <c>dtsTime *time.Time</c>。</summary>
    internal DateTime? _dtsTime;
}

/// <summary>Go: <c>type taskResult struct</c>（buildtask.go:44-54）。</summary>
internal sealed class TaskResult
{
    /// <summary>
    /// Go: <c>builder strings.Builder</c>（buildtask.go:45）（§6：→ <c>StringWriter</c>）。
    /// Go 的 <c>fmt.Fprintln(&amp;builder, …)</c> 恒写 "\n"；C# 的 <c>StringWriter</c> 默认 <c>NewLine</c> 是
    /// 平台相关的 "\r\n"，会把 <c>-b</c> 路径下的 <c>--explainFiles</c>/<c>--listEmittedFiles</c>/<c>-v</c>
    /// 输出写成分隔 CRLF（tsc CLI/日志换行恒 "\n"），故显式固定为 "\n"。
    /// </summary>
    internal StringWriter _builder = new() { NewLine = "\n" };

    /// <summary>Go: <c>reportStatus tsc.DiagnosticReporter</c>。</summary>
    internal Tsc.DiagnosticReporter _reportStatus = null!;

    /// <summary>Go: <c>diagnosticReporter tsc.DiagnosticReporter</c>。</summary>
    internal Tsc.DiagnosticReporter _diagnosticReporter = null!;

    /// <summary>Go: <c>exitStatus tsc.ExitStatus</c>。</summary>
    internal Tsc.ExitStatus _exitStatus;

    /// <summary>Go: <c>statistics *tsc.Statistics</c>。</summary>
    internal Tsc.Statistics? _statistics;

    /// <summary>Go: <c>program *incremental.Program</c>。</summary>
    internal Inc.Program? _program;

    /// <summary>Go: <c>buildKind buildKind</c>。</summary>
    internal BuildKind _buildKind;

    /// <summary>Go: <c>filesToDelete []string</c>。</summary>
    internal List<string> _filesToDelete = [];
}

/// <summary>Go: <c>type BuildTask struct</c>（buildtask.go:55-80）。Go 导出类型 → public；成员均为未导出 → internal。</summary>
public sealed class BuildTask
{
    internal string _config = "";
    internal ParsedCommandLine? _resolved;
    internal List<UpstreamTask> _upStream = [];
    internal List<BuildTask> _downStream = []; // Only set and used in watch mode
    internal UpToDateStatus? _status;
    internal ManualResetEventSlim _done = new();

    // task reporting
    internal TaskResult? _result;
    internal ManualResetEventSlim _built = new(); // Set when result is ready to be reported

    internal BuildInfoEntry? _buildInfoEntry;
    internal object _buildInfoEntryMu = new();
    internal List<string> _packageJsons = [];

    internal List<Diagnostic> _errors = [];
    internal volatile bool _pending;
    internal bool _isInitialCycle;
    internal object _downStreamUpdateMu = new();
    internal bool _dirty;

    // Go: `contentMapperProjectOnce sync.Once`（§4：bool 守卫 + 锁）。
    private readonly object _contentMapperProjectOnceMu = new();
    internal bool _contentMapperProjectOnceDone;
    internal CM.Project? _contentMapperProject;
    internal Exception? _contentMapperProjectErr;

    /// <summary>Go: <c>func (t *BuildTask) getContentMapperProject(orchestrator *Orchestrator) (contentmapper.Project, error)</c>（buildtask.go:82-95）。</summary>
    internal (CM.Project? Project, Exception? Error) GetContentMapperProject(Orchestrator orchestrator)
    {
        lock (_contentMapperProjectOnceMu)
        {
            if (!_contentMapperProjectOnceDone)
            {
                _contentMapperProjectOnceDone = true;
                if (orchestrator._contentMapperHost is not null && _resolved is not null && _resolved.ContentMappers() is { Length: > 0 } mappers)
                {
                    _contentMapperProject = orchestrator._contentMapperHost.Project(new CM.ProjectSpec
                    {
                        ConfigFileName = _resolved.ConfigName(),
                        Mappers = mappers,
                        CompilerOptions = _resolved.CompilerOptions(),
                    });
                }
            }
        }
        return (_contentMapperProject, _contentMapperProjectErr);
    }

    /// <summary>Go: <c>func (t *BuildTask) refreshContentMapperProject(orchestrator *Orchestrator)</c>（buildtask.go:96-100）。</summary>
    internal void RefreshContentMapperProject(Orchestrator orchestrator)
    {
        if (_contentMapperProject is not null)
        {
            try
            {
                _contentMapperProject.Refresh();
                // Go: `t.contentMapperProjectErr = t.contentMapperProject.Refresh()`——成功即清空旧错误。
                _contentMapperProjectErr = null;
            }
            catch (Exception err)
            {
                _contentMapperProjectErr = err;
            }
        }
    }

    /// <summary>Go: <c>func (t *BuildTask) waitOnUpstream()</c>（buildtask.go:102-106）。</summary>
    internal void WaitOnUpstream()
    {
        foreach (var upstream in _upStream)
        {
            upstream._task._done.Wait();
        }
    }

    /// <summary>Go: <c>func (t *BuildTask) unblockDownstream()</c>（buildtask.go:108-112）。</summary>
    internal void UnblockDownstream()
    {
        _pending = false;
        _isInitialCycle = false;
        _done.Set();
    }

    /// <summary>Go: <c>func (t *BuildTask) reportDiagnostic(err *ast.Diagnostic)</c>（buildtask.go:114-117）。</summary>
    internal void ReportDiagnostic(Diagnostic err)
    {
        _errors.Add(err);
        _result!._diagnosticReporter(err);
    }

    /// <summary>Go: <c>func (t *BuildTask) report(orchestrator, configPath, buildResult)</c>（buildtask.go:119-143）。</summary>
    internal void Report(Orchestrator orchestrator, Path configPath, OrchestratorResult buildResult)
    {
        if (_errors.Count > 0)
        {
            // Go: `append(core.IfElse(buildResult.Errors != nil, buildResult.Errors, []*ast.Diagnostic{}), t.errors...)`
            buildResult.Errors ??= [];
            buildResult.Errors.AddRange(_errors);
        }
        orchestrator._opts.Sys.Writer().Write(_result!._builder.ToString());
        if ((int)_result._exitStatus > (int)buildResult.Result.Status)
        {
            buildResult.Result.Status = _result._exitStatus;
        }
        if (_result._statistics is not null)
        {
            buildResult.Statistics.Aggregate(_result._statistics);
        }
        // If we built the program, or updated timestamps, or had errors, we need to
        // delete files that are no longer needed
        switch (_result._buildKind)
        {
            case BuildKind.Program:
                if (orchestrator._opts.Testing is not null)
                {
                    orchestrator._opts.Testing.OnProgram(_result._program!);
                }
                buildResult.Statistics.ProjectsBuilt++;
                break;
            case BuildKind.Pseudo:
                buildResult.Statistics.TimestampUpdates++;
                break;
        }
        buildResult.FilesToDelete.AddRange(_result._filesToDelete);
        _result = null;
    }

    /// <summary>Go: <c>func (t *BuildTask) buildProject(orchestrator, path)</c>（buildtask.go:145-174）。</summary>
    internal void BuildProject(Orchestrator orchestrator, Path path)
    {
        // Wait on upstream tasks to complete
        WaitOnUpstream();
        if (_pending)
        {
            _status = GetUpToDateStatus(orchestrator, path);
            ReportUpToDateStatus(orchestrator);
            if (!HandleStatusThatDoesntRequireBuild(orchestrator))
            {
                CompileAndEmit(orchestrator, path);
                UpdateDownstream(orchestrator, path);
            }
            else
            {
                if (_resolved is not null)
                {
                    foreach (var diagnostic in _resolved.GetConfigFileParsingDiagnostics())
                    {
                        ReportDiagnostic(diagnostic);
                    }
                }
                if (_errors.Count > 0)
                {
                    _result!._exitStatus = Tsc.ExitStatus.DiagnosticsPresent_OutputsSkipped;
                }
            }
        }
        else
        {
            if (_errors.Count > 0)
            {
                ReportUpToDateStatus(orchestrator);
                foreach (var err in _errors)
                {
                    // Should not add the diagnostics so just reporting
                    _result!._diagnosticReporter(err);
                }
            }
        }
        UnblockDownstream();
    }

    /// <summary>Go: <c>func (t *BuildTask) updateDownstream(orchestrator, path)</c>（buildtask.go:176-219）。</summary>
    internal void UpdateDownstream(Orchestrator orchestrator, Path path)
    {
        if (_isInitialCycle)
        {
            return;
        }
        if (orchestrator._opts.Command.BuildOptions!.StopBuildOnErrors.IsTrue() && _status!.IsError())
        {
            return;
        }
        if (_result!._program is null)
        {
            foreach (var downStream in _downStream)
            {
                lock (downStream._downStreamUpdateMu)
                {
                    downStream.ResetStatus();
                    downStream._pending = true;
                }
            }
            return;
        }

        foreach (var downStream in _downStream)
        {
            lock (downStream._downStreamUpdateMu)
            {
                if (downStream._status is not null)
                {
                    switch (downStream._status._kind)
                    {
                        case UpToDateStatusType.UpToDate:
                            if (!_result._program.HasChangedDtsFile())
                            {
                                downStream._status = new UpToDateStatus
                                {
                                    _kind = UpToDateStatusType.UpToDateWithUpstreamTypes,
                                    _data = downStream._status._data,
                                };
                                break;
                            }
                            // Go: `fallthrough`
                            goto case UpToDateStatusType.UpToDateWithUpstreamTypes;
                        case UpToDateStatusType.UpToDateWithUpstreamTypes:
                        case UpToDateStatusType.UpToDateWithInputFileText:
                            if (_result._program.HasChangedDtsFile())
                            {
                                downStream._status = new UpToDateStatus
                                {
                                    _kind = UpToDateStatusType.InputFileNewer,
                                    _data = new InputOutputName
                                    {
                                        _input = _config,
                                        _output = downStream._status.OldestOutputFileName(),
                                    },
                                };
                            }
                            break;
                        case UpToDateStatusType.UpstreamErrors:
                            var upstreamErrors = downStream._status.UpstreamErrors();
                            var refConfig = ResolveConfigFileNameOfProjectReference(upstreamErrors._ref);
                            if (orchestrator.ToPath(refConfig) == path)
                            {
                                downStream.ResetStatus();
                            }
                            break;
                    }
                }
                downStream._pending = true;
            }
        }
    }

    /// <summary>Go: <c>func (t *BuildTask) compileAndEmit(orchestrator, path)</c>（buildtask.go:221-294）。</summary>
    internal void CompileAndEmit(Orchestrator orchestrator, Path path)
    {
        _errors = [];
        if (orchestrator._opts.Command.BuildOptions!.Verbose.IsTrue())
        {
            _result!._reportStatus(NewCompilerDiagnostic(Building_project_0, orchestrator.RelativeFileName(_config)));
        }

        // Real build
        var compileTimes = new Tsc.CompileTimes();
        var (configTime, _) = orchestrator._host._configTimes.Load(path);
        compileTimes.ConfigTime = configTime;
        var buildInfoReadStart = orchestrator._opts.Sys.Now();
        Inc.Program? oldProgram = null;
        var (contentMapperProject, err) = GetContentMapperProject(orchestrator);
        if (err is not null)
        {
            ReportDiagnostic(Comp.ContentMapperProjectDiagnostic(err));
            _status = new UpToDateStatus { _kind = UpToDateStatusType.BuildErrors };
            _result!._exitStatus = Tsc.ExitStatus.DiagnosticsPresent_OutputsSkipped;
            return;
        }
        var compilerHost = new ProjectCompilerHost
        {
            _host = orchestrator._host,
            _trace = GetTraceWithWriterFromSys(_result!._builder, orchestrator._opts.Command.Locale, orchestrator._opts.Testing),
            _contentMapperProject = contentMapperProject,
        };
        if (!orchestrator._opts.Command.BuildOptions!.Force.IsTrue())
        {
            oldProgram = ReadBuildInfoProgram(_resolved!, orchestrator._host, compilerHost);
        }
        compileTimes.BuildInfoReadTime = orchestrator._opts.Sys.Now() - buildInfoReadStart;
        var parseStart = orchestrator._opts.Sys.Now();
        var program = Comp.NewProgram(new ProgramOptions
        {
            Config = _resolved!,
            Host = compilerHost,
        });
        compileTimes.ParseTime = orchestrator._opts.Sys.Now() - parseStart;
        var changesComputeStart = orchestrator._opts.Sys.Now();
        _result!._program = Inc.Program.NewProgram(program, oldProgram, orchestrator._host, orchestrator._opts.Sys.Now, orchestrator._opts.Testing is not null);
        compileTimes.ChangesComputeTime = orchestrator._opts.Sys.Now() - changesComputeStart;

        var (result, statistics) = EmitAndReportStatistics(new Tsc.EmitInput
        {
            Sys = orchestrator._opts.Sys,
            ProgramLike = _result._program,
            Program = program,
            Config = _resolved!,
            ReportDiagnostic = ReportDiagnostic,
            ReportErrorSummary = QuietDiagnosticsReporter,
            Writer = _result._builder,
            WriteFile = (fileName, text, data) => WriteFile(orchestrator, fileName, text, data),
            CompileTimes = compileTimes,
            Testing = orchestrator._opts.Testing,
            TestingMTimesCache = orchestrator._host._mTimes,
        });
        _result._exitStatus = result.Status;
        _result._statistics = statistics;
        _packageJsons = [.. _result._program.PackageJsonLookupPaths()];
        if ((!program.Options().NoEmit.IsTrue() || result.Diagnostics.Length == 0) &&
            (result.EmitResult!.EmittedFiles.Length > 0 || _status!._kind != UpToDateStatusType.OutOfDateBuildInfoWithErrors))
        {
            // Update time stamps for rest of the outputs
            UpdateTimeStamps(orchestrator, result.EmitResult.EmittedFiles, Updating_unchanged_output_timestamps_of_project_0);
        }
        _result._buildKind = BuildKind.Program;
        if (result.Status == Tsc.ExitStatus.DiagnosticsPresent_OutputsSkipped || result.Status == Tsc.ExitStatus.DiagnosticsPresent_OutputsGenerated)
        {
            _status = new UpToDateStatus { _kind = UpToDateStatusType.BuildErrors };
        }
        else
        {
            string oldestOutputFileName;
            if (result.EmitResult!.EmittedFiles.Length > 0)
            {
                oldestOutputFileName = result.EmitResult.EmittedFiles[0];
            }
            else
            {
                oldestOutputFileName = FirstOrNilSeq(_resolved!.GetOutputFileNames());
            }
            _status = new UpToDateStatus { _kind = UpToDateStatusType.UpToDate, _data = oldestOutputFileName };
        }
    }

    /// <summary>Go: <c>func (t *BuildTask) handleStatusThatDoesntRequireBuild(orchestrator) bool</c>（buildtask.go:296-344）。</summary>
    internal bool HandleStatusThatDoesntRequireBuild(Orchestrator orchestrator)
    {
        switch (_status!._kind)
        {
            case UpToDateStatusType.UpToDate:
                if (orchestrator._opts.Command.BuildOptions!.Dry.IsTrue())
                {
                    _result!._reportStatus(NewCompilerDiagnostic(Project_0_is_up_to_date, _config));
                }
                return true;
            case UpToDateStatusType.UpstreamErrors:
                var upstreamStatus = _status!.UpstreamErrors();
                if (orchestrator._opts.Command.BuildOptions!.Verbose.IsTrue())
                {
                    _result!._reportStatus(NewCompilerDiagnostic(
                        IfElse(
                            upstreamStatus._refHasUpstreamErrors,
                            Skipping_build_of_project_0_because_its_dependency_1_was_not_built,
                            Skipping_build_of_project_0_because_its_dependency_1_has_errors),
                        orchestrator.RelativeFileName(_config),
                        orchestrator.RelativeFileName(upstreamStatus._ref)));
                }
                return true;
            case UpToDateStatusType.Solution:
                return true;
            case UpToDateStatusType.ConfigFileNotFound:
                ReportDiagnostic(NewCompilerDiagnostic(File_0_not_found, _config));
                return true;
        }

        // update timestamps
        if (_status!.IsPseudoBuild())
        {
            if (orchestrator._opts.Command.BuildOptions!.Dry.IsTrue())
            {
                _result!._reportStatus(NewCompilerDiagnostic(A_non_dry_build_would_update_timestamps_for_output_of_project_0, _config));
                _status = new UpToDateStatus { _kind = UpToDateStatusType.UpToDate };
                return true;
            }

            UpdateTimeStamps(orchestrator, null, Updating_output_timestamps_of_project_0);
            _status = new UpToDateStatus { _kind = UpToDateStatusType.UpToDate, _data = _status._data };
            _result!._buildKind = BuildKind.Pseudo;
            return true;
        }

        if (orchestrator._opts.Command.BuildOptions!.Dry.IsTrue())
        {
            _result!._reportStatus(NewCompilerDiagnostic(A_non_dry_build_would_build_project_0, _config));
            _status = new UpToDateStatus { _kind = UpToDateStatusType.UpToDate };
            return true;
        }

        return false;
    }

    /// <summary>Go: <c>func (t *BuildTask) getUpToDateStatus(orchestrator, configPath) *upToDateStatus</c>（buildtask.go:346-619）。</summary>
    internal UpToDateStatus GetUpToDateStatus(Orchestrator orchestrator, Path configPath)
    {
        if (_status is not null)
        {
            return _status;
        }
        // Config file not found
        if (_resolved is null)
        {
            return new UpToDateStatus { _kind = UpToDateStatusType.ConfigFileNotFound };
        }

        // Solution - nothing to build
        if (_resolved.FileNames().Length == 0 && _resolved.ProjectReferences() is not null)
        {
            return new UpToDateStatus { _kind = UpToDateStatusType.Solution };
        }

        foreach (var upstream in _upStream)
        {
            if (orchestrator._opts.Command.BuildOptions!.StopBuildOnErrors.IsTrue() && upstream._task._status!.IsError())
            {
                // Upstream project has errors, so we cannot build this project
                return new UpToDateStatus
                {
                    _kind = UpToDateStatusType.UpstreamErrors,
                    _data = new UpstreamErrors
                    {
                        _ref = _resolved.ProjectReferences()![upstream._refIndex].Path,
                        _refHasUpstreamErrors = upstream._task._status._kind == UpToDateStatusType.UpstreamErrors,
                    },
                };
            }
        }

        if (orchestrator._opts.Command.BuildOptions!.Force.IsTrue())
        {
            return new UpToDateStatus { _kind = UpToDateStatusType.ForceBuild };
        }

        // Check the build info
        var buildInfoPath = _resolved.GetBuildInfoFileName();
        var getBuildInfoDirectory = Memoize(() => GetDirectoryPath(GetNormalizedAbsolutePath(buildInfoPath, orchestrator._comparePathsOptions.CurrentDirectory)));
        var (buildInfo, buildInfoTime) = LoadOrStoreBuildInfo(orchestrator, configPath, buildInfoPath);
        if (buildInfo is null)
        {
            return new UpToDateStatus { _kind = UpToDateStatusType.OutputMissing, _data = buildInfoPath };
        }

        // build info version
        if (!buildInfo.IsValidVersion())
        {
            return new UpToDateStatus { _kind = UpToDateStatusType.TsVersionOutputOfDate, _data = buildInfo.Version };
        }

        // If a configured content mapper's identity has changed, files it produced may be stale.
        var (contentMapperProject, err) = GetContentMapperProject(orchestrator);
        List<string>? contentMapperIdentities = null;
        var identityErr = false;
        try
        {
            contentMapperIdentities = ContentMapperIdentities(contentMapperProject);
        }
        catch (Exception e)
        {
            // Go: `contentMapperIdentities, identityErr := ...; if identityErr != nil { t.contentMapperProjectErr = identityErr }`
            _contentMapperProjectErr = e;
            identityErr = true;
        }
        if (err is not null || identityErr || !buildInfo.ContentMapperIdentitiesMatch(contentMapperIdentities))
        {
            return new UpToDateStatus { _kind = UpToDateStatusType.OutOfDateOptions, _data = buildInfoPath };
        }

        // Report errors if build info indicates errors
        if (buildInfo.Errors || // Errors that need to be reported irrespective of "--noCheck"
            (!_resolved.CompilerOptions()!.NoCheck.IsTrue() && (buildInfo.SemanticErrors || buildInfo.CheckPending))) // Errors without --noCheck
        {
            return new UpToDateStatus { _kind = UpToDateStatusType.OutOfDateBuildInfoWithErrors, _data = buildInfoPath };
        }

        if (_resolved.CompilerOptions()!.IsIncremental())
        {
            if (!buildInfo.IsIncremental())
            {
                // Program options out of date
                return new UpToDateStatus { _kind = UpToDateStatusType.OutOfDateOptions, _data = buildInfoPath };
            }

            // Errors need to be reported if build info has errors
            if ((_resolved.CompilerOptions()!.GetEmitDeclarations() && buildInfo.EmitDiagnosticsPerFile is not null) || // Always reported errors
                (!_resolved.CompilerOptions()!.NoCheck.IsTrue() && // Semantic errors if not --noCheck
                    (buildInfo.ChangeFileSet is not null || buildInfo.SemanticDiagnosticsPerFile is not null)))
            {
                return new UpToDateStatus { _kind = UpToDateStatusType.OutOfDateBuildInfoWithErrors, _data = buildInfoPath };
            }

            // Pending emit files
            if (!_resolved.CompilerOptions()!.NoEmit.IsTrue() &&
                (buildInfo.ChangeFileSet is not null || buildInfo.AffectedFilesPendingEmit is not null))
            {
                return new UpToDateStatus { _kind = UpToDateStatusType.OutOfDateBuildInfoWithPendingEmit, _data = buildInfoPath };
            }

            // Some of the emit files like source map or dts etc are not yet done
            if (buildInfo.IsEmitPending(_resolved, getBuildInfoDirectory()))
            {
                return new UpToDateStatus { _kind = UpToDateStatusType.OutOfDateOptions, _data = buildInfoPath };
            }
        }
        var inputTextUnchanged = false;
        var oldestOutputFileAndTime = new FileAndTime { _file = buildInfoPath, _time = buildInfoTime };
        var newestInputFileAndTime = new FileAndTime();
        var seenRoots = new Set<Path>();
        var getBuildInfoRootInfoReader = Memoize(() => buildInfo.GetBuildInfoRootInfoReader(getBuildInfoDirectory(), orchestrator._comparePathsOptions));
        foreach (var inputFile in _resolved.FileNames())
        {
            var inputTime = orchestrator._host.GetMTime(inputFile);
            if (inputTime == default)
            {
                return new UpToDateStatus { _kind = UpToDateStatusType.InputFileMissing, _data = inputFile };
            }
            var inputPath = orchestrator.ToPath(inputFile);
            if (inputTime > oldestOutputFileAndTime._time)
            {
                var version = "";
                var currentVersion = "";
                if (buildInfo.IsIncremental())
                {
                    var (buildInfoFileInfo, resolvedInputPath) = getBuildInfoRootInfoReader().GetBuildInfoFileInfo(inputPath);
                    if (buildInfoFileInfo?.GetFileInfo() is { } fileInfo && fileInfo.Version() != "")
                    {
                        version = fileInfo.Version();
                        if (orchestrator._host.FS().TryReadFile(resolvedInputPath.Value, out var text))
                        {
                            currentVersion = ComputeHash(text, orchestrator._opts.Testing is not null);
                            if (version == currentVersion)
                            {
                                inputTextUnchanged = true;
                            }
                        }
                    }
                }

                if (version == "" || version != currentVersion)
                {
                    return new UpToDateStatus
                    {
                        _kind = UpToDateStatusType.InputFileNewer,
                        _data = new InputOutputName { _input = inputFile, _output = buildInfoPath },
                    };
                }
            }
            if (inputTime > newestInputFileAndTime._time)
            {
                newestInputFileAndTime = new FileAndTime { _file = inputFile, _time = inputTime };
            }
            seenRoots.Add(inputPath);
        }

        foreach (var root in getBuildInfoRootInfoReader().Roots())
        {
            if (!seenRoots.Has(root))
            {
                // File was root file when project was built but its not any more
                return new UpToDateStatus
                {
                    _kind = UpToDateStatusType.OutOfDateRoots,
                    _data = new InputOutputName { _input = root.Value, _output = buildInfoPath },
                };
            }
        }

        if (buildInfo.IsIncremental())
        {
            var resolvedRoots = new Set<Path>();
            foreach (var root in getBuildInfoRootInfoReader().Roots())
            {
                var (_, resolved) = getBuildInfoRootInfoReader().GetBuildInfoFileInfo(root);
                if (resolved.Value != "")
                {
                    resolvedRoots.Add(resolved);
                }
            }
            for (var index = 0; index < (buildInfo.FileInfos?.Count ?? 0); index++)
            {
                var buildInfoFileInfo = buildInfo.FileInfos![index];
                var buildInfoFileName = buildInfo.FileNames![index];
                // Lib files bundled with the compiler can change only with the version of the compiler,
                // which is already verified with buildInfo.Version
                if (IsBuildInfoFileNameDefaultLibrary(buildInfoFileName))
                {
                    continue;
                }
                var inputFile = GetNormalizedAbsolutePath(buildInfoFileName, getBuildInfoDirectory());
                var inputPath = orchestrator.ToPath(inputFile);
                // Root files are already checked
                if (seenRoots.Has(inputPath) || resolvedRoots.Has(inputPath))
                {
                    continue;
                }
                if (IsContentMapperSupplementalBuildInfoPath(inputPath, getBuildInfoRootInfoReader().Roots()) &&
                    !orchestrator._host.FS().FileExists(inputFile))
                {
                    continue;
                }
                var inputTime = orchestrator._host.GetMTime(inputFile);
                if (inputTime == default)
                {
                    // Input file that was part of the program is missing (eg: dependency was removed)
                    return new UpToDateStatus { _kind = UpToDateStatusType.InputFileMissing, _data = inputFile };
                }

                if (inputTime > oldestOutputFileAndTime._time)
                {
                    var currentVersion = "";
                    var version = buildInfoFileInfo.GetFileInfo()!.Version();
                    if (version != "")
                    {
                        if (orchestrator._host.FS().TryReadFile(inputFile, out var text))
                        {
                            currentVersion = ComputeHash(text, orchestrator._opts.Testing is not null);
                        }
                    }
                    if (version == "" || version != currentVersion)
                    {
                        return new UpToDateStatus
                        {
                            _kind = UpToDateStatusType.InputFileNewer,
                            _data = new InputOutputName { _input = inputFile, _output = buildInfoPath },
                        };
                    }
                    inputTextUnchanged = true;
                }
            }
        }

        if (!_resolved.CompilerOptions()!.IsIncremental())
        {
            // Check output file stamps
            foreach (var outputFile in _resolved.GetOutputFileNames())
            {
                var outputTime = orchestrator._host.GetMTime(outputFile);
                if (outputTime == default)
                {
                    // Output file missing
                    return new UpToDateStatus { _kind = UpToDateStatusType.OutputMissing, _data = outputFile };
                }

                if (outputTime < newestInputFileAndTime._time)
                {
                    // Output file is older than input file
                    return new UpToDateStatus
                    {
                        _kind = UpToDateStatusType.InputFileNewer,
                        _data = new InputOutputName { _input = newestInputFileAndTime._file, _output = outputFile },
                    };
                }

                if (outputTime < oldestOutputFileAndTime._time)
                {
                    oldestOutputFileAndTime = new FileAndTime { _file = outputFile, _time = outputTime };
                }
            }
        }

        var refDtsUnchanged = false;
        foreach (var upstream in _upStream)
        {
            if (upstream._task._status!._kind == UpToDateStatusType.Solution)
            {
                // Not dependent on the status or this upstream project
                // (eg: expected cycle was detected and hence skipped, or is solution)
                continue;
            }

            // If the upstream project's newest file is older than our oldest output,
            // we can't be out of date because of it
            // inputTime will not be present if we just built this project or updated timestamps
            // - in that case we do want to either build or update timestamps
            var refInputOutputFileAndTime = upstream._task._status.InputOutputFileAndTime();
            if (refInputOutputFileAndTime is not null && refInputOutputFileAndTime._input._time != default && refInputOutputFileAndTime._input._time < oldestOutputFileAndTime._time)
            {
                continue;
            }

            // Check if tsbuildinfo path is shared, then we need to rebuild
            if (HasConflictingBuildInfo(orchestrator, upstream._task))
            {
                // We have an output older than an upstream output - we are out of date
                return new UpToDateStatus
                {
                    _kind = UpToDateStatusType.InputFileNewer,
                    _data = new InputOutputName
                    {
                        _input = _resolved.ProjectReferences()![upstream._refIndex].Path,
                        _output = oldestOutputFileAndTime._file,
                    },
                };
            }

            // If the upstream project has only change .d.ts files, and we've built
            // *after* those files, then we're "pseudo up to date" and eligible for a fast rebuild
            var newestDtsChangeTime = upstream._task.GetLatestChangedDtsMTime(orchestrator);
            if (newestDtsChangeTime != default && newestDtsChangeTime < oldestOutputFileAndTime._time)
            {
                refDtsUnchanged = true;
                continue;
            }

            // We have an output older than an upstream output - we are out of date
            return new UpToDateStatus
            {
                _kind = UpToDateStatusType.InputFileNewer,
                _data = new InputOutputName
                {
                    _input = _resolved.ProjectReferences()![upstream._refIndex].Path,
                    _output = oldestOutputFileAndTime._file,
                },
            };
        }

        UpToDateStatus? CheckInputFileTime(string inputFile)
        {
            var inputTime = orchestrator._host.GetMTime(inputFile);
            if (inputTime > oldestOutputFileAndTime._time)
            {
                // Output file is older than input file
                return new UpToDateStatus
                {
                    _kind = UpToDateStatusType.InputFileNewer,
                    _data = new InputOutputName { _input = inputFile, _output = oldestOutputFileAndTime._file },
                };
            }
            return null;
        }

        var configStatus = CheckInputFileTime(_config);
        if (configStatus is not null)
        {
            return configStatus;
        }

        foreach (var extendedConfig in _resolved.ExtendedSourceFiles() ?? [])
        {
            var extendedConfigStatus = CheckInputFileTime(extendedConfig);
            if (extendedConfigStatus is not null)
            {
                return extendedConfigStatus;
            }
        }

        foreach (var packageJson in buildInfo.GetPackageJsons(getBuildInfoDirectory()))
        {
            var packageJsonTime = orchestrator._host.GetMTime(packageJson);
            if (packageJsonTime == default)
            {
                return new UpToDateStatus { _kind = UpToDateStatusType.InputFileMissing, _data = packageJson };
            }
            if (packageJsonTime > oldestOutputFileAndTime._time)
            {
                return new UpToDateStatus
                {
                    _kind = UpToDateStatusType.InputFileNewer,
                    _data = new InputOutputName { _input = packageJson, _output = oldestOutputFileAndTime._file },
                };
            }
        }
        foreach (var packageJson in buildInfo.GetMissingPackageJsons(getBuildInfoDirectory()))
        {
            if (orchestrator._host.GetMTime(packageJson) != default)
            {
                return new UpToDateStatus
                {
                    _kind = UpToDateStatusType.InputFileNewer,
                    _data = new InputOutputName { _input = packageJson, _output = oldestOutputFileAndTime._file },
                };
            }
        }
        _packageJsons = [.. buildInfo.GetPackageJsons(getBuildInfoDirectory())];
        _packageJsons.AddRange(buildInfo.GetMissingPackageJsons(getBuildInfoDirectory()));

        return new UpToDateStatus
        {
            _kind = IfElse(
                refDtsUnchanged,
                UpToDateStatusType.UpToDateWithUpstreamTypes,
                IfElse(inputTextUnchanged, UpToDateStatusType.UpToDateWithInputFileText, UpToDateStatusType.UpToDate)),
            _data = new InputOutputFileAndTime
            {
                _input = newestInputFileAndTime,
                _output = oldestOutputFileAndTime,
                _buildInfo = buildInfoPath,
            },
        };
    }

    /// <summary>Go: <c>func isContentMapperSupplementalBuildInfoPath(inputPath tspath.Path, roots iter.Seq[tspath.Path]) bool</c>（buildtask.go:621-636）。</summary>
    internal static bool IsContentMapperSupplementalBuildInfoPath(Path inputPath, IEnumerable<Path> roots)
    {
        foreach (var root in roots)
        {
            var prefix = root.Value + ".";
            // Go: `strings.CutPrefix(string(inputPath), string(root)+".")`
            if (!inputPath.Value.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }
            var suffix = inputPath.Value.AsSpan(prefix.Length);
            // Go: `strings.Cut(suffix, ".")`
            var dot = suffix.IndexOf('.');
            if (dot < 0)
            {
                continue;
            }
            var index = suffix[..dot];
            var extension = suffix[(dot + 1)..];
            if (extension.Length == 0)
            {
                continue;
            }
            // Go: `strconv.Atoi(index)`
            if (int.TryParse(index, out _) && IsSupportedVirtualExtension("." + extension.ToString()))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Go: <c>func (t *BuildTask) reportUpToDateStatus(orchestrator)</c>（buildtask.go:638-748）。</summary>
    internal void ReportUpToDateStatus(Orchestrator orchestrator)
    {
        if (!orchestrator._opts.Command.BuildOptions!.Verbose.IsTrue())
        {
            return;
        }
        switch (_status!._kind)
        {
            case UpToDateStatusType.ConfigFileNotFound:
                _result!._reportStatus(NewCompilerDiagnostic(
                    Project_0_is_out_of_date_because_config_file_does_not_exist,
                    orchestrator.RelativeFileName(_config)));
                break;
            case UpToDateStatusType.UpstreamErrors:
                var upstreamStatus = _status!.UpstreamErrors();
                _result!._reportStatus(NewCompilerDiagnostic(
                    IfElse(
                        upstreamStatus._refHasUpstreamErrors,
                        Project_0_can_t_be_built_because_its_dependency_1_was_not_built,
                        Project_0_can_t_be_built_because_its_dependency_1_has_errors),
                    orchestrator.RelativeFileName(_config),
                    orchestrator.RelativeFileName(upstreamStatus._ref)));
                break;
            case UpToDateStatusType.BuildErrors:
                _result!._reportStatus(NewCompilerDiagnostic(
                    Project_0_is_out_of_date_because_it_has_errors,
                    orchestrator.RelativeFileName(_config)));
                break;
            case UpToDateStatusType.UpToDate:
                // This is to ensure skipping verbose log for projects that were built,
                // and then some other package changed but this package doesnt need update
                if (_status!.InputOutputFileAndTime() is { } inputOutputFileAndTime)
                {
                    _result!._reportStatus(NewCompilerDiagnostic(
                        Project_0_is_up_to_date_because_newest_input_1_is_older_than_output_2,
                        orchestrator.RelativeFileName(_config),
                        orchestrator.RelativeFileName(inputOutputFileAndTime._input._file),
                        orchestrator.RelativeFileName(inputOutputFileAndTime._output._file)));
                }
                break;
            case UpToDateStatusType.UpToDateWithUpstreamTypes:
                _result!._reportStatus(NewCompilerDiagnostic(
                    Project_0_is_up_to_date_with_d_ts_files_from_its_dependencies,
                    orchestrator.RelativeFileName(_config)));
                break;
            case UpToDateStatusType.UpToDateWithInputFileText:
                _result!._reportStatus(NewCompilerDiagnostic(
                    Project_0_is_up_to_date_but_needs_to_update_timestamps_of_output_files_that_are_older_than_input_files,
                    orchestrator.RelativeFileName(_config)));
                break;
            case UpToDateStatusType.InputFileMissing:
                _result!._reportStatus(NewCompilerDiagnostic(
                    Project_0_is_out_of_date_because_input_1_does_not_exist,
                    orchestrator.RelativeFileName(_config),
                    orchestrator.RelativeFileName((string)_status!._data!)));
                break;
            case UpToDateStatusType.OutputMissing:
                _result!._reportStatus(NewCompilerDiagnostic(
                    Project_0_is_out_of_date_because_output_file_1_does_not_exist,
                    orchestrator.RelativeFileName(_config),
                    orchestrator.RelativeFileName((string)_status!._data!)));
                break;
            case UpToDateStatusType.InputFileNewer:
                var inputOutput = _status!.InputOutputName()!;
                _result!._reportStatus(NewCompilerDiagnostic(
                    Project_0_is_out_of_date_because_output_1_is_older_than_input_2,
                    orchestrator.RelativeFileName(_config),
                    orchestrator.RelativeFileName(inputOutput._output),
                    orchestrator.RelativeFileName(inputOutput._input)));
                break;
            case UpToDateStatusType.OutOfDateBuildInfoWithPendingEmit:
                _result!._reportStatus(NewCompilerDiagnostic(
                    Project_0_is_out_of_date_because_buildinfo_file_1_indicates_that_some_of_the_changes_were_not_emitted,
                    orchestrator.RelativeFileName(_config),
                    orchestrator.RelativeFileName((string)_status!._data!)));
                break;
            case UpToDateStatusType.OutOfDateBuildInfoWithErrors:
                _result!._reportStatus(NewCompilerDiagnostic(
                    Project_0_is_out_of_date_because_buildinfo_file_1_indicates_that_program_needs_to_report_errors,
                    orchestrator.RelativeFileName(_config),
                    orchestrator.RelativeFileName((string)_status!._data!)));
                break;
            case UpToDateStatusType.OutOfDateOptions:
                _result!._reportStatus(NewCompilerDiagnostic(
                    Project_0_is_out_of_date_because_buildinfo_file_1_indicates_there_is_change_in_compilerOptions,
                    orchestrator.RelativeFileName(_config),
                    orchestrator.RelativeFileName((string)_status!._data!)));
                break;
            case UpToDateStatusType.OutOfDateRoots:
                var outOfDateRootsInputOutput = _status!.InputOutputName()!;
                _result!._reportStatus(NewCompilerDiagnostic(
                    Project_0_is_out_of_date_because_buildinfo_file_1_indicates_that_file_2_was_root_file_of_compilation_but_not_any_more,
                    orchestrator.RelativeFileName(_config),
                    orchestrator.RelativeFileName(outOfDateRootsInputOutput._output),
                    orchestrator.RelativeFileName(outOfDateRootsInputOutput._input)));
                break;
            case UpToDateStatusType.TsVersionOutputOfDate:
                _result!._reportStatus(NewCompilerDiagnostic(
                    Project_0_is_out_of_date_because_output_for_it_was_generated_with_version_1_that_differs_with_current_version_2,
                    orchestrator.RelativeFileName(_config),
                    orchestrator.RelativeFileName((string)_status!._data!),
                    Version()));
                break;
            case UpToDateStatusType.ForceBuild:
                _result!._reportStatus(NewCompilerDiagnostic(
                    Project_0_is_being_forcibly_rebuilt,
                    orchestrator.RelativeFileName(_config)));
                break;
            case UpToDateStatusType.Solution:
                // Does not need to report status
                break;
            default:
                Debug.Fail($"Unknown up to date status kind: {_status!._kind}");
                break;
        }
    }

    /// <summary>Go: <c>func (t *BuildTask) canUpdateJsDtsOutputTimestamps() bool</c>（buildtask.go:750-752）。</summary>
    internal bool CanUpdateJsDtsOutputTimestamps() =>
        !_resolved!.CompilerOptions()!.NoEmit.IsTrue() && !_resolved.CompilerOptions()!.IsIncremental();

    /// <summary>Go: <c>func (t *BuildTask) updateTimeStamps(orchestrator, emittedFiles, verboseMessage)</c>（buildtask.go:754-787）。</summary>
    internal void UpdateTimeStamps(Orchestrator orchestrator, string[]? emittedFiles, Message verboseMessage)
    {
        var emitted = NewSetFromItems<string>(emittedFiles ?? []);
        var verboseMessageReported = false;
        var buildInfoName = _resolved!.GetBuildInfoFileName();
        var now = orchestrator._opts.Sys.Now();
        void UpdateTimeStamp(string file)
        {
            if (emitted.Has(file))
            {
                return;
            }
            if (!verboseMessageReported && orchestrator._opts.Command.BuildOptions!.Verbose.IsTrue())
            {
                _result!._reportStatus(NewCompilerDiagnostic(verboseMessage, orchestrator.RelativeFileName(_config)));
                verboseMessageReported = true;
            }
            // Go: `err := ...SetMTime(...); if err == nil { … }` —— C# 的 Host.SetMTime 失败抛异常，
            // Go 在这里**忽略**该错误（既不记账也不上报），故吞掉异常直接返回。
            try
            {
                orchestrator._host.SetMTime(file, now);
            }
            catch (Exception)
            {
                return;
            }
            if (file == buildInfoName)
            {
                lock (_buildInfoEntryMu)
                {
                    if (_buildInfoEntry is not null)
                    {
                        _buildInfoEntry._mTime = now;
                    }
                }
            }
            else if (StoreOutputTimeStamp(orchestrator))
            {
                orchestrator._host.StoreMTime(file, now);
            }
        }

        if (CanUpdateJsDtsOutputTimestamps())
        {
            foreach (var outputFile in _resolved.GetOutputFileNames())
            {
                UpdateTimeStamp(outputFile);
            }
        }
        UpdateTimeStamp(_resolved.GetBuildInfoFileName());
    }

    /// <summary>Go: <c>func (t *BuildTask) cleanProject(orchestrator, path)</c>（buildtask.go:789-801）。</summary>
    internal void CleanProject(Orchestrator orchestrator, Path path)
    {
        if (_resolved is null)
        {
            ReportDiagnostic(NewCompilerDiagnostic(File_0_not_found, _config));
            _result!._exitStatus = Tsc.ExitStatus.DiagnosticsPresent_OutputsSkipped;
            return;
        }

        var inputs = NewSetFromItems(Map<string, Path>(_resolved.FileNames(), orchestrator.ToPath));
        foreach (var outputFile in _resolved.GetOutputFileNames())
        {
            CleanProjectOutput(orchestrator, outputFile, inputs);
        }
        CleanProjectOutput(orchestrator, _resolved.GetBuildInfoFileName(), inputs);
    }

    /// <summary>Go: <c>func (t *BuildTask) cleanProjectOutput(orchestrator, outputFile, inputs)</c>（buildtask.go:803-819）。</summary>
    internal void CleanProjectOutput(Orchestrator orchestrator, string outputFile, Set<Path> inputs)
    {
        var outputPath = orchestrator.ToPath(outputFile);
        // If output name is same as input file name, do not delete and ignore the error
        if (inputs.Has(outputPath))
        {
            return;
        }
        if (orchestrator._host.FS().FileExists(outputFile))
        {
            if (!orchestrator._opts.Command.BuildOptions!.Dry.IsTrue())
            {
                // Go: `err := ...Remove(...); if err != nil { … }` —— FS.Remove 失败抛异常（§3.2）。
                try
                {
                    orchestrator._host.FS().Remove(outputFile);
                }
                catch (Exception)
                {
                    ReportDiagnostic(NewCompilerDiagnostic(Failed_to_delete_file_0, outputFile));
                }
            }
            else
            {
                _result!._filesToDelete.Add(outputFile);
            }
        }
    }

    /// <summary>Go: <c>func (t *BuildTask) updateWatch(orchestrator, oldCache)</c>（buildtask.go:821-829）。</summary>
    internal void UpdateWatch(Orchestrator orchestrator, SyncMap<Path, DateTime> oldCache)
    {
        if (_resolved is not null)
        {
            if (CanUpdateJsDtsOutputTimestamps())
            {
                foreach (var outputFile in _resolved.GetOutputFileNames())
                {
                    orchestrator._host.StoreMTimeFromOldCache(outputFile, oldCache);
                }
            }
        }
    }

    /// <summary>Go: <c>func (t *BuildTask) resetStatus()</c>（buildtask.go:831-835）。</summary>
    internal void ResetStatus()
    {
        _status = null;
        _pending = true;
        _errors = [];
    }

    /// <summary>Go: <c>func (t *BuildTask) resetConfig(orchestrator, path)</c>（buildtask.go:837-840）。</summary>
    internal void ResetConfig(Orchestrator orchestrator, Path path)
    {
        _dirty = true;
        orchestrator._host._resolvedReferences.Delete(path);
    }

    /// <summary>Go: <c>func (t *BuildTask) loadOrStoreBuildInfo(orchestrator, configPath, buildInfoFileName)</c>（buildtask.go:842-859）。</summary>
    internal (Inc.BuildInfo? BuildInfo, DateTime MTime) LoadOrStoreBuildInfo(Orchestrator orchestrator, Path configPath, string buildInfoFileName)
    {
        var path = orchestrator.ToPath(buildInfoFileName);
        lock (_buildInfoEntryMu)
        {
            if (_buildInfoEntry is not null && _buildInfoEntry._path == path)
            {
                return (_buildInfoEntry._buildInfo, _buildInfoEntry._mTime);
            }
            _buildInfoEntry = new BuildInfoEntry
            {
                _buildInfo = NewBuildInfoReader(orchestrator._host).ReadBuildInfo(_resolved!),
                _path = path,
            };
            var mTime = default(DateTime);
            if (_buildInfoEntry._buildInfo is not null)
            {
                mTime = orchestrator._host.GetMTime(buildInfoFileName);
            }
            _buildInfoEntry._mTime = mTime;
            return (_buildInfoEntry._buildInfo, mTime);
        }
    }

    /// <summary>Go: <c>func (t *BuildTask) onBuildInfoEmit(orchestrator, buildInfoFileName, buildInfo, hasChangedDtsFile)</c>（buildtask.go:861-877）。</summary>
    internal void OnBuildInfoEmit(Orchestrator orchestrator, string buildInfoFileName, Inc.BuildInfo buildInfo, bool hasChangedDtsFile)
    {
        lock (_buildInfoEntryMu)
        {
            DateTime? dtsTime = null;
            var mTime = orchestrator._opts.Sys.Now();
            if (hasChangedDtsFile)
            {
                dtsTime = mTime;
            }
            else if (_buildInfoEntry is not null)
            {
                dtsTime = _buildInfoEntry._dtsTime;
            }
            _buildInfoEntry = new BuildInfoEntry
            {
                _buildInfo = buildInfo,
                _path = orchestrator.ToPath(buildInfoFileName),
                _mTime = mTime,
                _dtsTime = dtsTime,
            };
        }
    }

    /// <summary>Go: <c>func (t *BuildTask) hasConflictingBuildInfo(orchestrator, upstream)</c>（buildtask.go:879-884）。</summary>
    internal bool HasConflictingBuildInfo(Orchestrator orchestrator, BuildTask upstream)
    {
        if (_buildInfoEntry is not null && upstream._buildInfoEntry is not null)
        {
            return _buildInfoEntry._path == upstream._buildInfoEntry._path;
        }
        return false;
    }

    /// <summary>Go: <c>func (t *BuildTask) getLatestChangedDtsMTime(orchestrator) time.Time</c>（buildtask.go:886-900）。</summary>
    internal DateTime GetLatestChangedDtsMTime(Orchestrator orchestrator)
    {
        lock (_buildInfoEntryMu)
        {
            if (_buildInfoEntry!._dtsTime is not null)
            {
                return _buildInfoEntry._dtsTime.Value;
            }
            var dtsTime = orchestrator._host.GetMTime(
                GetNormalizedAbsolutePath(
                    _buildInfoEntry._buildInfo!.LatestChangedDtsFile,
                    GetDirectoryPath(_buildInfoEntry._path.Value)));
            _buildInfoEntry._dtsTime = dtsTime;
            return dtsTime;
        }
    }

    /// <summary>Go: <c>func (t *BuildTask) storeOutputTimeStamp(orchestrator) bool</c>（buildtask.go:902-904）。</summary>
    internal bool StoreOutputTimeStamp(Orchestrator orchestrator) =>
        orchestrator._opts.Command.CompilerOptions!.Watch.IsTrue() && !_resolved!.CompilerOptions()!.IsIncremental();

    /// <summary>
    /// Go: <c>func (t *BuildTask) writeFile(orchestrator, fileName, text, data) error</c>（buildtask.go:906-918）。
    /// C# 的 <c>Compiler.WriteFile</c> 委托没有 error 通道（§3.2）：写失败的异常直接向外传播，
    /// emitter 会捕获它并报 <c>Could_not_write_file_0_Colon_1</c>。
    /// </summary>
    internal void WriteFile(Orchestrator orchestrator, string fileName, string text, WriteFileData? data)
    {
        orchestrator._host.FS().WriteFile(fileName, text);
        if (data is not null && data.BuildInfo is not null)
        {
            OnBuildInfoEmit(orchestrator, fileName, (Inc.BuildInfo)data.BuildInfo, _result!._program!.HasChangedDtsFile());
        }
        else if (StoreOutputTimeStamp(orchestrator))
        {
            // Store time stamps
            orchestrator._host.StoreMTime(fileName, orchestrator._opts.Sys.Now());
        }
    }
}
