// ported from: internal/execute/tsctests/runner.go
//
// 与 Go 的偏离（逐条）：
// 1) Go 的 `t.Run(...)` 里 `t.Parallel()`（并发子测试）→ C# 顺序执行（baseline 逐字节比较要求确定性；
//    docs/harness.md §4 同口径）。Go 的 `core.NewWorkGroup(false)` 把"跑编辑"与"跑全量重算"两个闭包并发排队
//    → 这里顺序执行：两条闭包互不写同一缓冲（只写 baselineBuilder 的闭包只有第一条），输出顺序逐字一致。
// 2) `t.Context()` → `Core.Context.Background`（`withCancellation` 到 `IHarnessTest.CancellationToken`，与
//    Go 的"子测试结束即取消、顺带拆掉 content mapper host"语义相同）。
// 3) `fmt.Fprint(baselineBuilder, …)` → `StringBuilder.Append`；Go 的 `%v` 打印 bool 是 "true"/"false"，
//    C# 的 `bool.ToString()` 是 "True"/"False" → 统一走 `BoolStr`。
// 4) `execute.CommandLine(ctx, sys, args, sys)` → `Exec.CommandLine(...)`；`tsc.ExitStatusXxx` →
//    `TscExitStatus.Xxx`；`panic("UnknownExitStatus …")` → `Debug.Fail(...)`。
// 5) `baseline.DiffText` → `Baseline.DiffText`；`baseline.Run(t, file, content, Options{Subfolder})` →
//    `Baseline.Run(t, file, content, new BaselineOptions { Subfolder = … })`。
// 6) `t.Errorf(...)` → `t.Error(...)`（IHarnessTest 只有 Error(string)）。
// 7) `nonIncrementalSys.fs.writtenFiles.ToSlice()` → `nonIncrementalSys.Fs.WrittenFiles.ToSlice()`（已是数组，
//    Go 的 slices.Sort 用 Array.Sort + StringComparer.Ordinal 保持字节序）。
// 未移植：无（runner.go 的每个函数都有对应物）。
namespace TypeScript.Harness.TscTests;

using global::System;
using global::System.Collections.Generic;
using global::System.Text;
using TypeScript.Core;
using TypeScript.TestUtil;
using TypeScript.TestUtil.Baseline;
using TypeScript.Execute.WatchManager;

using static TypeScript.Core.Core;
using static TypeScript.TSPath.TSPath;

using Exec = TypeScript.Execute.Execute;
using TscExitStatus = TypeScript.Execute.Tsc.ExitStatus;
using TscResult = TypeScript.Execute.Tsc.CommandLineResult;

/// <summary>Go: <c>type tscEdit struct</c>（runner.go:19-24）。</summary>
public sealed class TscEdit
{
    /// <summary>Go: <c>caption string</c>。</summary>
    public string Caption = "";

    /// <summary>Go: <c>commandLineArgs []string</c>（nil = 沿用用例的命令行）。</summary>
    public string[]? CommandLineArgs;

    /// <summary>Go: <c>edit func(*TestSys)</c>。</summary>
    public Action<TestSys>? Edit;

    /// <summary>Go: <c>expectedDiff string</c>。</summary>
    public string ExpectedDiff = "";
}

/// <summary>Go: <c>var noChange = &amp;tscEdit{caption: "no change"}</c>。</summary>
public static class TscEdits
{
    public static readonly TscEdit NoChange = new() { Caption = "no change" };

    /// <summary>Go: <c>var noChangeOnlyEdit = []*tscEdit{noChange}</c>。</summary>
    public static TscEdit[] NoChangeOnlyEdit() => [NoChange];
}

/// <summary>Go: <c>type tscInput struct</c>（runner.go:34-44）。</summary>
public sealed class TscInput
{
    /// <summary>Go: <c>subScenario string</c>。</summary>
    public string SubScenario = "";

    /// <summary>Go: <c>commandLineArgs []string</c>（nil = 无参数，与空切片在基线上等价）。</summary>
    public string[]? CommandLineArgs;

    /// <summary>Go: <c>files FileMap</c>。</summary>
    public FileMap Files = new();

    /// <summary>Go: <c>cwd string</c>（空 = 默认 "/home/src/workspaces/project"）。</summary>
    public string Cwd = "";

    /// <summary>Go: <c>edits []*tscEdit</c>。</summary>
    public TscEdit[] Edits = [];

    /// <summary>Go: <c>env map[string]string</c>（nil = 无环境变量）。</summary>
    public Dictionary<string, string>? Env;

    /// <summary>Go: <c>outputIsTTY *bool</c>（nil = 用默认 true）。</summary>
    public bool? OutputIsTTY;

    /// <summary>Go: <c>ignoreCase bool</c>。</summary>
    public bool IgnoreCase;

    /// <summary>Go: <c>windowsStyleRoot string</c>。</summary>
    public string WindowsStyleRoot = "";

    /// <summary>Go: <c>func (test *tscInput) executeCommand(ctx, sys, baselineBuilder, commandLineArgs) tsc.CommandLineResult</c>。</summary>
    public TscResult ExecuteCommand(Context ctx, TestSys sys, StringBuilder baselineBuilder, string[]? commandLineArgs)
    {
        var args = commandLineArgs ?? [];
        baselineBuilder.Append("tsgo ").Append(string.Join(" ", args)).Append('\n');
        var result = Exec.CommandLine(ctx, sys, args, sys);
        switch (result.Status)
        {
            case TscExitStatus.Success:
                baselineBuilder.Append("ExitStatus:: Success");
                break;
            case TscExitStatus.DiagnosticsPresent_OutputsSkipped:
                baselineBuilder.Append("ExitStatus:: DiagnosticsPresent_OutputsSkipped");
                break;
            case TscExitStatus.DiagnosticsPresent_OutputsGenerated:
                baselineBuilder.Append("ExitStatus:: DiagnosticsPresent_OutputsGenerated");
                break;
            case TscExitStatus.InvalidProject_OutputsSkipped:
                baselineBuilder.Append("ExitStatus:: InvalidProject_OutputsSkipped");
                break;
            case TscExitStatus.ProjectReferenceCycle_OutputsSkipped:
                baselineBuilder.Append("ExitStatus:: ProjectReferenceCycle_OutputsSkipped");
                break;
            case TscExitStatus.NotImplemented:
                baselineBuilder.Append("ExitStatus:: NotImplemented");
                break;
            default:
                Debug.Fail($"UnknownExitStatus {(int)result.Status}");
                break;
        }

        return result;
    }

    /// <summary>Go: <c>func (test *tscInput) run(t *testing.T, scenario string)</c>（runner.go:70-155）。</summary>
    public void Run(IHarnessTest t, string scenario)
    {
        t.Run(GetBaselineSubFolder() + "/" + SubScenario, tt => RunScenario(tt, scenario));
    }

    private void RunScenario(IHarnessTest t, string scenario)
    {
        var ctx = WithCancellation(Context.Background, t.CancellationToken);

        // initial test tsc compile
        var baselineBuilder = new StringBuilder();
        var sys = TestSys.NewTestSys(this, forIncrementalCorrectness: false);
        baselineBuilder
            .Append("currentDirectory::").Append(sys.GetCurrentDirectory())
            .Append("\nuseCaseSensitiveFileNames::").Append(BoolStr(sys.FS().UseCaseSensitiveFileNames()))
            .Append("\nInput::\n");
        sys.BaselineFSWithDiff(baselineBuilder);
        var result = ExecuteCommand(ctx, sys, baselineBuilder, CommandLineArgs);
        sys.SerializeState(baselineBuilder);
        if (result.Watcher is not null && sys.MockWatchBackend.HasWatches())
        {
            baselineBuilder.Append(sys.MockWatchBackend.WatchState());
        }

        var unexpectedDiff = new StringBuilder();
        unexpectedDiff.Append(sys.BaselinePrograms(baselineBuilder, "Initial build"));

        // Go 的 `for … range test.Edits` 在 nil 切片上迭代零次（`edits: nil` 是合法用例），C# 里要先落成空数组。
        var edits = Edits ?? [];
        for (var index = 0; index < edits.Length; index++)
        {
            var do_ = edits[index];
            sys.ClearOutput();
            var commandLineArgs = do_.CommandLineArgs ?? CommandLineArgs;

            baselineBuilder.Append($"\n\nEdit [{index}]:: {do_.Caption}\n");
            do_.Edit?.Invoke(sys);
            var changedPaths = sys.Differ.ChangedPaths();
            sys.BaselineFSWithDiff(baselineBuilder);

            if (result.Watcher is null)
            {
                ExecuteCommand(ctx, sys, baselineBuilder, commandLineArgs);
            }
            else
            {
                sys.MockWatchBackend.SendChangedPaths(changedPaths);
                result.Watcher.DoCycle();
            }

            sys.SerializeState(baselineBuilder);
            if (result.Watcher is not null && sys.MockWatchBackend.HasWatches())
            {
                baselineBuilder.Append(sys.MockWatchBackend.WatchState());
            }

            unexpectedDiff.Append(sys.BaselinePrograms(baselineBuilder, $"Edit [{index}]:: {do_.Caption}\n"));

            // Compute build with all the edits
            var nonIncrementalSys = TestSys.NewTestSys(this, forIncrementalCorrectness: true);
            for (var i = 0; i < index + 1; i++)
            {
                edits[i].Edit?.Invoke(nonIncrementalSys);
            }

            Exec.CommandLine(ctx, nonIncrementalSys, commandLineArgs ?? [], nonIncrementalSys);

            var diff = GetDiffForIncremental(sys, nonIncrementalSys);
            if (diff.Length != 0)
            {
                baselineBuilder.Append($"\n\nDiff:: {(do_.ExpectedDiff.Length == 0 ? "!!! Unexpected diff, please review and either fix or write explanation as expectedDiff !!!" : do_.ExpectedDiff)}\n");
                baselineBuilder.Append(diff);
                if (do_.ExpectedDiff.Length == 0)
                {
                    unexpectedDiff.Append($"Edit [{index}]:: {do_.Caption}\n!!! Unexpected diff, please review and either fix or write explanation as expectedDiff !!!\n{diff}\n");
                }
            }
            else if (do_.ExpectedDiff.Length != 0)
            {
                baselineBuilder.Append($"\n\nDiff:: {do_.ExpectedDiff} !!! Diff not found but explanation present, please review and remove the explanation !!!\n");
                unexpectedDiff.Append($"Edit [{index}]:: {do_.Caption}\n!!! Diff not found but explanation present, please review and remove the explanation !!!\n");
            }
        }

        Baseline.Run(
            t,
            SubScenario.Replace(" ", "-") + ".js",
            baselineBuilder.ToString(),
            new BaselineOptions { Subfolder = GetBaselineSubFolder() + "/" + scenario });
        if (unexpectedDiff.Length != 0)
        {
            t.Error($"Test {SubScenario} has unexpected diff {unexpectedDiff} with incremental build, please review the baseline file");
        }
    }

    /// <summary>Go: <c>func getDiffForIncremental(incrementalSys, nonIncrementalSys *TestSys) string</c>（runner.go:157-186）。</summary>
    private static string GetDiffForIncremental(TestSys incrementalSys, TestSys nonIncrementalSys)
    {
        var diffBuilder = new StringBuilder();

        var nonIncrementalOutputs = nonIncrementalSys.Fs.WrittenFiles.ToSlice();
        Array.Sort(nonIncrementalOutputs, StringComparer.Ordinal);
        foreach (var nonIncrementalOutput in nonIncrementalOutputs)
        {
            if (FileExtensionIs(nonIncrementalOutput, ExtensionTsBuildInfo) ||
                nonIncrementalOutput.EndsWith(".readable.baseline.txt", StringComparison.Ordinal))
            {
                // Just check existence
                if (!incrementalSys.FsFromFileMap().FileExists(nonIncrementalOutput))
                {
                    diffBuilder.Append(Baseline.DiffText(
                        "nonIncremental " + nonIncrementalOutput,
                        "incremental " + nonIncrementalOutput,
                        "Exists",
                        ""));
                    diffBuilder.Append('\n');
                }
            }
            else
            {
                if (!nonIncrementalSys.FsFromFileMap().TryReadFile(nonIncrementalOutput, out var nonIncrementalText))
                {
                    // Go: panic("Written file not found " + nonIncrementalOutput)
                    Debug.Fail("Written file not found " + nonIncrementalOutput);
                }

                var incrementalText = incrementalSys.FsFromFileMap().TryReadFile(nonIncrementalOutput, out var found)
                    ? found
                    : "";
                if (incrementalText != nonIncrementalText)
                {
                    diffBuilder.Append(Baseline.DiffText(
                        "nonIncremental " + nonIncrementalOutput,
                        "incremental " + nonIncrementalOutput,
                        nonIncrementalText,
                        incrementalText));
                    diffBuilder.Append('\n');
                }
            }
        }

        var incrementalOutput = incrementalSys.GetOutput(forComparing: true);
        var nonIncrementalOutputText = nonIncrementalSys.GetOutput(forComparing: true);
        if (incrementalOutput != nonIncrementalOutputText)
        {
            diffBuilder.Append(Baseline.DiffText(
                "nonIncremental.output.txt",
                "incremental.output.txt",
                nonIncrementalOutputText,
                incrementalOutput));
        }

        return diffBuilder.ToString();
    }

    /// <summary>Go: <c>func (test *tscInput) getBaselineSubFolder() string</c>（runner.go:188-205）。</summary>
    public string GetBaselineSubFolder()
    {
        var commandName = "tsc";
        if (ContainsAny(CommandLineArgs, "-b", "--b", "-build", "--build"))
        {
            commandName = "tsbuild";
        }

        var w = "";
        if (ContainsAny(CommandLineArgs, "-w", "--w", "-watch", "--watch"))
        {
            w = "Watch";
        }

        return commandName + w;
    }

    private static bool ContainsAny(string[]? args, params string[] candidates)
    {
        if (args is null)
        {
            return false;
        }

        foreach (var arg in args)
        {
            foreach (var candidate in candidates)
            {
                if (arg == candidate)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Go 的 <c>%v</c> 对 bool 的打印（见文件头偏离 3）。</summary>
    internal static string BoolStr(bool value) => value ? "true" : "false";
}
