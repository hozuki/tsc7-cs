// 非 Go 源文件：`--suite tsctests` / `--suite tscbuild` 的运行入口（对应 Go 侧
// `go test ./internal/execute/tsctests` 里的 `TestTsc*` / `TestBuild*` 两个大组）。
//
// 与 Go 的对应关系：
//   * 一个 Go 测试函数（如 `TestTscCommandline`）→ 一个用例文件（`TscCases/TscCommandLineCases.cs`）。
//   * Go 的 `testCase.run(t, "commandLine")` → registry 里的一个条目，名字 = `<TestFunc>/<scenario>/<subScenario>`
//     （与 `go test -run 'TestTscCommandline/commandLine/<subScenario>'` 同形）。
//   * `--case <子串>` = Go 的 `-run`；`--limit/--offset` 分批；`--diff N` 限制差异打印条数。
//
// baseline 口径（与 Program.cs 的 compiler/conformance 完全一致，只是根目录是**多层**的）：
//   `Baseline.Run` 只在"生成内容 != reference"时才写 `baselines/local/<subfolder>/<file>` 并报错；相等时两者都不写。
//   所以"recorded（本次生成/比较过） + local 里没有该文件 + reference 有内容" == **逐字节一致**。
//   tsctests 的 baseline 根是 `reference/{tsc,tscWatch,tsbuild,tsbuildWatch}/<scenario>/<用例>.js`（实读确认，见
//   artifacts/tsctests-plan.md §2），因此比较按 tracking 清单逐条走 reference↔local，而不是按文件名索引。
namespace TypeScript.Harness;

using global::System;
using global::System.Collections.Generic;
using global::System.IO;
using global::System.Linq;
using TypeScript.Repo;
using TypeScript.TestRunner;
using TypeScript.TestUtil;
using TypeScript.TestUtil.Baseline;
using TypeScript.Harness.TscTests;

using static TypeScript.TSPath.TSPath;

/// <summary>`--suite tsctests` / `--suite tscbuild` 的运行入口。</summary>
internal static class TscTestsSuite
{
    /// <summary>返回进程退出码（0 = 全绿）。</summary>
    public static int Run(string suite, string[] caseFilters, int limit, int offset, int maxDiffs, bool listOnly, int caseTimeoutSeconds)
    {
        var all = TscCaseRegistry.ForSuite(suite).ToList();
        var selected = new List<TscCaseEntry>();
        var matched = 0;
        foreach (var entry in all)
        {
            if (caseFilters.Length > 0 &&
                !caseFilters.Any(f => entry.Name.Contains(f, StringComparison.Ordinal) ||
                                      f.Contains(entry.Name, StringComparison.Ordinal)))
            {
                continue;
            }

            if (matched++ < offset)
            {
                continue;
            }

            selected.Add(entry);
            if (selected.Count >= limit)
            {
                break;
            }
        }

        Console.WriteLine($"[{suite}] registry = {all.Count} case(s)");
        Console.WriteLine($"[{suite}] selected = {selected.Count} case(s)");
        if (listOnly)
        {
            foreach (var entry in selected)
            {
                Console.WriteLine("  " + entry.Name);
            }

            return 0;
        }

        if (selected.Count == 0)
        {
            Console.Error.WriteLine($"no {suite} case matched --case filter(s): {string.Join(", ", caseFilters)}");
            return 2;
        }

        // 陈旧 local 输出会造成假差异：清空四个 tsctests 专属的 local 根（其它套件不写这些目录）。
        foreach (var root in new[] { "tsc", "tscWatch", "tsbuild", "tsbuildWatch" })
        {
            var dir = CombinePaths(Repo.TestDataPath(), "baselines", "local", root);
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        var report = new RunReport();
        var root2 = HarnessTest.Root(report, [], only: null);

        // baseline tracking：本套件自己保证 tracking 目录存在（否则 `recorded` 为空，无法判定"生成过"）。
        // 必须在 `Baseline` 类型首次使用**之前**设置环境变量（`Baseline._trackingDir` 是静态只读、首次访问时初始化）。
        var trackingDir = Environment.GetEnvironmentVariable("TSGO_BASELINE_TRACKING_DIR") ?? "";
        if (trackingDir.Length == 0)
        {
            trackingDir = CombinePaths(Repo.TestDataPath(), "baselines", "local", "_tracking-tsctests");
            if (Directory.Exists(trackingDir))
            {
                Directory.Delete(trackingDir, recursive: true);
            }

            Directory.CreateDirectory(trackingDir);
            Environment.SetEnvironmentVariable("TSGO_BASELINE_TRACKING_DIR", trackingDir);
        }

        var trackingCleanup = Baseline.Track();
        try
        {
            // 进度与单用例超时看门狗（仅集成观测；--case-timeout 0 表示不启用，与其它套件共用实现）。
            HarnessProgress.Configure(caseTimeoutSeconds, selected.Count);
            foreach (var entry in selected)
            {
                HarnessProgress.BeginCase(suite, entry.Name);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                root2.Run(entry.Name, entry.Body);
                watch.Stop();
                HarnessProgress.EndCase(suite, entry.Name, watch.Elapsed.TotalMilliseconds);
            }
        }
        finally
        {
            trackingCleanup();
        }

        // ---- baseline 对拍（按 tracking 清单逐条 reference↔local）----
        var recorded = ReadRecordedBaselines(trackingDir);
        var identical = 0;
        var mismatch = 0;
        var notGenerated = 0;
        var diffLines = new List<string>();
        var failedCases = 0;
        foreach (var name in report.CasesRun)
        {
            if (report.CaseFailed(name))
            {
                failedCases++;
            }
        }

        foreach (var relative in recorded.Order(StringComparer.Ordinal))
        {
            var referencePath = CombinePaths(Repo.TestDataPath(), "baselines", "reference", relative);
            var localPath = CombinePaths(Repo.TestDataPath(), "baselines", "local", relative);
            if (File.Exists(localPath))
            {
                mismatch++;
                if (diffLines.Count < maxDiffs)
                {
                    diffLines.Add($"  DIFF [{relative}] {FirstDiff(referencePath, localPath)}");
                }
            }
            else if (File.Exists(localPath + ".delete"))
            {
                mismatch++;
                if (diffLines.Count < maxDiffs)
                {
                    diffLines.Add($"  DIFF [{relative}] local 写了 `<no content>`，但 reference 有内容");
                }
            }
            else if (!File.Exists(referencePath))
            {
                notGenerated++;
                if (diffLines.Count < maxDiffs)
                {
                    diffLines.Add($"  NEW [{relative}] reference 里没有这个 baseline（新建）");
                }
            }
            else
            {
                identical++;
            }
        }

        Console.WriteLine();
        Console.WriteLine($"[{suite}] baselines recorded={recorded.Count} byte-identical={identical} " +
            $"mismatch={mismatch} new={notGenerated}");
        foreach (var line in diffLines)
        {
            Console.WriteLine(line);
        }

        Console.WriteLine();
        Console.WriteLine($"cases selected = {selected.Count}, cases executed = {report.CasesRun.Count}, cases with failures = {failedCases}");
        Console.WriteLine($"test failures = {report.Failures.Count}, skips = {report.Skips.Count}");
        if (failedCases > 0)
        {
            Console.WriteLine("failed cases (name: #failures, first message):");
            foreach (var caseName in report.CasesRun.Order(StringComparer.Ordinal))
            {
                var messages = report.Failures
                    .Where(f => f.Name == caseName || f.Name.StartsWith(caseName + "/", StringComparison.Ordinal))
                    .Select(f => f.Message)
                    .ToList();
                if (messages.Count > 0)
                {
                    Console.WriteLine($"  FAIL {caseName}: {messages.Count} failure(s); first: {Detail(messages[0])}");
                }
            }
        }

        var ok = report.Failures.Count == 0 && mismatch == 0 && notGenerated == 0 && recorded.Count > 0;
        Console.WriteLine(ok ? "RESULT: PASS" : "RESULT: FAIL");
        return ok ? 0 : 1;
    }

    /// <summary>读 baseline tracking 落盘的清单（相对 baselines/reference 的路径）。</summary>
    private static List<string> ReadRecordedBaselines(string trackingDir)
    {
        var recorded = new List<string>();
        if (trackingDir.Length == 0 || !Directory.Exists(trackingDir))
        {
            return recorded;
        }

        foreach (var path in Directory.EnumerateFiles(trackingDir, "*.txt"))
        {
            foreach (var line in File.ReadLines(path))
            {
                var name = line.Trim();
                if (name.Length > 0)
                {
                    recorded.Add(name);
                }
            }
        }

        return recorded;
    }

    /// <summary>逐字节比较，返回首个差异的描述；一致返回 null（local 不存在时由调用方判为 identical）。</summary>
    private static string? FirstDiff(string referencePath, string localPath)
    {
        var reference = File.ReadAllBytes(referencePath);
        var local = File.ReadAllBytes(localPath);
        var common = Math.Min(reference.Length, local.Length);
        for (var i = 0; i < common; i++)
        {
            if (reference[i] != local[i])
            {
                var line = 1;
                for (var j = 0; j < i; j++)
                {
                    if (reference[j] == (byte)'\n')
                    {
                        line++;
                    }
                }

                return $"first diff at line {line} (byte offset {i}): " +
                    $"expected 0x{reference[i]:X2} ({Escape(reference[i])}) vs actual 0x{local[i]:X2} ({Escape(local[i])}); " +
                    $"reference line: {LineAt(reference, i)}";
            }
        }

        if (reference.Length != local.Length)
        {
            var line = 1;
            for (var j = 0; j < common; j++)
            {
                if (reference[j] == (byte)'\n')
                {
                    line++;
                }
            }

            return $"length differs at byte offset {common} (line {line}): expected {reference.Length} bytes, actual {local.Length} bytes";
        }

        return null;
    }

    private static string LineAt(byte[] bytes, int index)
    {
        var start = index;
        while (start > 0 && bytes[start - 1] != (byte)'\n' && bytes[start - 1] != (byte)'\r')
        {
            start--;
        }

        var end = index;
        while (end < bytes.Length && bytes[end] != (byte)'\n' && bytes[end] != (byte)'\r')
        {
            end++;
        }

        var text = System.Text.Encoding.UTF8.GetString(bytes, start, end - start);
        return "\"" + (text.Length > 120 ? text[..120] + "…" : text) + "\"";
    }

    private static string Escape(byte b) => b switch
    {
        (byte)'\r' => "CR",
        (byte)'\n' => "LF",
        (byte)'\t' => "TAB",
        _ => b is >= 32 and < 127 ? ((char)b).ToString() : "·",
    };

    private static string Detail(string message)
    {
        var single = message.Replace("\r\n", " | ", StringComparison.Ordinal).Replace('\n', ' ');
        return single.Length > 700 ? single[..700] + "…" : single;
    }
}
