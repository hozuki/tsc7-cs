// ported from: internal/execute/tsc/emit.go
//
// 与 Go 的偏离（docs/architecture.md §2/§3）：
// 1) 本命名空间里有名为 `System` 的接口（Go 的 `tsc.System`），会遮蔽根命名空间 `System` →
//    本文件一律 `using global::System;`/`using global::System.IO;` 且禁止 `System.` 前缀（同 Compile.cs 偏离 1）。
// 2) `io.Writer` → `System.IO.TextWriter`。
// 3) `context.Context` → `TypeScript.Core.Context`（Go 的 `context.Background()` → `Context.Background`）。
// 4) Go 的 `defer tr.Push(...)()` → `try/finally` + `Action?`（Tracing.Push 返回的收尾委托）。
// 5) `compiler.Program`/`compiler.ProgramLike`/`compiler.SortAndDeduplicateDiagnostics` 等与被 import 的
//    命名空间同名 → 用别名 `Comp`（见下）；`incremental.Program` → `IncProgram`。
// 6) Go 的 `fmt.Fprintln(w, "TSFILE:", x)`（两参数插空格）→ 字符串拼接。
// 7) `runtime.MemStats` 的 `Alloc`/`Mallocs` 两个字段在 EmitAndReportStatistics 里就地取材（见 Statistics.cs 偏离）。
using global::System;
using global::System.IO;
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.TSPath;
using TypeScript.TsOptions;
using Loc = TypeScript.Locale.Locale;
using Tr = TypeScript.Tracing.Tracing;
using Phase = TypeScript.Tracing.Phase;
using Comp = TypeScript.Compiler.Compiler;
using IncProgram = TypeScript.Execute.Incremental.Program;
using Path = TypeScript.TSPath.Path;

using static TypeScript.TSPath.TSPath;

namespace TypeScript.Execute.Tsc;

public static partial class Tsc
{
    /// <summary>Go: <c>func GetTraceWithWriterFromSys(w, locale, testing) func(msg *diagnostics.Message, args ...any)</c>（emit.go:21-29）。</summary>
    public static TraceFunc GetTraceWithWriterFromSys(TextWriter w, Loc locale, CommandLineTesting? testing)
    {
        if (testing is null)
        {
            return (msg, args) => w.WriteLine(msg.Localize(locale, args));
        }
        return testing.GetTrace(w, locale);
    }

    /// <summary>Go: <c>func EmitAndReportStatistics(input EmitInput) (CompileAndEmitResult, *Statistics)</c>（emit.go:46-72）。</summary>
    public static (CompileAndEmitResult Result, Statistics? Statistics) EmitAndReportStatistics(EmitInput input)
    {
        Statistics? statistics = null;
        var result = EmitFilesAndReportErrors(input);
        if (result.Status != ExitStatus.Success)
        {
            // compile exited early
            return (result, null);
        }
        result._times!._totalTime = input.Sys.SinceStart();

        var options = input.Config.CompilerOptions()!;
        if (options.Diagnostics.IsTrue() || options.ExtendedDiagnostics.IsTrue())
        {
            var memStats = new MemStats();
            // GC must be called twice to allow things to settle.
            GC.Collect();
            GC.Collect();
            memStats.Alloc = (ulong)GC.GetTotalMemory(false);
            memStats.Mallocs = (ulong)GC.GetTotalAllocatedBytes();

            statistics = StatisticsFromProgram(input, memStats);
            statistics.Report(input.Writer, input.Testing);
        }

        if (result.EmitResult!.EmitSkipped && result.Diagnostics.Length > 0)
        {
            result.Status = ExitStatus.DiagnosticsPresent_OutputsSkipped;
        }
        else if (result.Diagnostics.Length > 0)
        {
            result.Status = ExitStatus.DiagnosticsPresent_OutputsGenerated;
        }
        return (result, statistics);
    }
}

/// <summary>Go: <c>type EmitInput struct</c>（emit.go:31-44）。</summary>
public sealed class EmitInput
{
    /// <summary>Go: <c>Sys System</c>。</summary>
    public System Sys = null!;

    /// <summary>Go: <c>ProgramLike compiler.ProgramLike</c>。</summary>
    public ProgramLike ProgramLike = null!;

    /// <summary>Go: <c>Program *compiler.Program</c>。</summary>
    public Program Program = null!;

    /// <summary>Go: <c>Config *tsoptions.ParsedCommandLine</c>。</summary>
    public ParsedCommandLine Config = null!;

    /// <summary>Go: <c>ReportDiagnostic DiagnosticReporter</c>。</summary>
    public DiagnosticReporter ReportDiagnostic = null!;

    /// <summary>Go: <c>ReportErrorSummary DiagnosticsReporter</c>。</summary>
    public DiagnosticsReporter ReportErrorSummary = null!;

    /// <summary>Go: <c>Writer io.Writer</c>。</summary>
    public TextWriter Writer = null!;

    /// <summary>Go: <c>WriteFile compiler.WriteFile</c>。</summary>
    public WriteFile WriteFile = null!;

    /// <summary>Go: <c>CompileTimes *CompileTimes</c>（调用方总是给出实例；Go 里它也是必填）。</summary>
    public CompileTimes CompileTimes = null!;

    /// <summary>Go: <c>Testing CommandLineTesting</c>（nil = 非测试模式）。</summary>
    public CommandLineTesting? Testing;

    /// <summary>Go: <c>TestingMTimesCache *collections.SyncMap[tspath.Path, time.Time]</c>。</summary>
    public SyncMap<Path, DateTime>? TestingMTimesCache;

    /// <summary>Go: <c>Tracing *tracing.Tracing</c>。</summary>
    public TypeScript.Tracing.Tracing? Tracing;
}

public static partial class Tsc
{
    /// <summary>Go: <c>func EmitFilesAndReportErrors(input EmitInput) (result CompileAndEmitResult)</c>（emit.go:74-142）。</summary>
    public static CompileAndEmitResult EmitFilesAndReportErrors(EmitInput input)
    {
        var result = new CompileAndEmitResult { _times = input.CompileTimes };
        var ctx = Context.Background;

        var allDiagnostics = Comp.GetDiagnosticsOfAnyProgram(
            ctx,
            input.ProgramLike,
            null,
            false,
            (_, file) =>
            {
                // Options diagnostics include global diagnostics (even though we collect them separately),
                // and global diagnostics create checkers, which then bind all of the files. Do this binding
                // early so we can track the time.
                Action? doneTrace = null;
                if (input.Tracing is not null)
                {
                    doneTrace = input.Tracing.Push(Phase.Bind, "bindSourceFiles", null, true);
                }
                try
                {
                    var bindStart = input.Sys.Now();
                    var diags = input.ProgramLike.GetBindDiagnostics(ctx, file);
                    result._times!._bindTime = input.Sys.Now() - bindStart;
                    return diags;
                }
                finally
                {
                    doneTrace?.Invoke();
                }
            },
            (_, file) =>
            {
                Action? doneTrace = null;
                if (input.Tracing is not null)
                {
                    doneTrace = input.Tracing.Push(Phase.Check, "checkSourceFiles", null, true);
                }
                try
                {
                    var checkStart = input.Sys.Now();
                    var diags = input.ProgramLike.GetSemanticDiagnostics(ctx, file);
                    result._times!._checkTime = input.Sys.Now() - checkStart;
                    if (input.ProgramLike is IncProgram program)
                    {
                        var nestedEmitTime = program.TakeNestedEmitTime();
                        if (nestedEmitTime > result._times!._checkTime)
                        {
                            result._times!._checkTime = default;
                        }
                        else
                        {
                            result._times!._checkTime -= nestedEmitTime;
                        }
                        result._times!._emitTime += nestedEmitTime;
                    }
                    return diags;
                }
                finally
                {
                    doneTrace?.Invoke();
                }
            });

        EmitResult? emitResult = new EmitResult { EmitSkipped = true, Diagnostics = [] };
        if (!input.ProgramLike.Options().ListFilesOnly.IsTrue())
        {
            var emitStart = input.Sys.Now();
            emitResult = input.ProgramLike.Emit(ctx, new EmitOptions { WriteFile = input.WriteFile });
            result._times!._emitTime += input.Sys.Now() - emitStart;
        }
        if (emitResult is not null)
        {
            allDiagnostics = [.. allDiagnostics, .. emitResult.Diagnostics];
        }
        if (input.Testing is not null)
        {
            input.Testing.OnEmittedFiles(emitResult, input.TestingMTimesCache);
        }

        allDiagnostics = Comp.SortAndDeduplicateDiagnostics(allDiagnostics);
        foreach (var diagnostic in allDiagnostics)
        {
            input.ReportDiagnostic(diagnostic);
        }

        ListFiles(input, emitResult!);

        input.ReportErrorSummary(allDiagnostics);
        result.Diagnostics = allDiagnostics;
        result.EmitResult = emitResult;
        result.Status = ExitStatus.Success;
        return result;
    }

    /// <summary>Go: <c>func listFiles(input EmitInput, emitResult *compiler.EmitResult)</c>（emit.go:144-161）。</summary>
    private static void ListFiles(EmitInput input, EmitResult emitResult)
    {
        if (input.Testing is not null)
        {
            input.Testing.OnListFilesStart(input.Writer);
        }
        try
        {
            var options = input.Program.Options();
            if (options.ListEmittedFiles.IsTrue())
            {
                foreach (var file in emitResult.EmittedFiles)
                {
                    input.Writer.WriteLine("TSFILE: " + GetNormalizedAbsolutePath(file, input.Program.GetCurrentDirectory()));
                }
            }
            if (options.ExplainFiles.IsTrue())
            {
                input.Program.ExplainFiles(input.Writer, input.Config.Locale);
            }
            else if (options.ListFiles.IsTrue() || options.ListFilesOnly.IsTrue())
            {
                foreach (var file in input.Program.GetSourceFiles())
                {
                    input.Writer.WriteLine(file.FileName());
                }
            }
        }
        finally
        {
            input.Testing?.OnListFilesEnd(input.Writer);
        }
    }
}
