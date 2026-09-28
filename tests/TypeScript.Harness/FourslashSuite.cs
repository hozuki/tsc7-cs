// fourslash 用例的运行入口（`--suite fourslash`）。
//
// 与 `internal/fourslash/tests/*_test.go` 的对应：
//   Go 的 `go test -run TestXxx ./internal/fourslash/tests`（每个用例一个顶层测试函数，内部跑大栈线程）
//   → 这里 `--case <子串>` 过滤 + `HarnessTest.Root(report, ...).Run(name, body)`。
//
// baseline 口径（与 Program.cs 的 compiler/conformance 一致）：
//   `baseline.Run` 只在"生成内容 != reference"时才写 `baselines/local/**` 并失败；相等时两者都不写。
//   所以对某个用例：
//     * 它在 `baselines/reference/fourslash/**` 下有 N 个 reference 文件（按 command 子目录分）；
//     * reference 存在、且 local 里没有同名文件 → **逐字节一致**；
//     * local 里有同名文件 → 不一致，打印首个差异。
//   运行前会清空 `baselines/local/fourslash`（fourslash 是唯一写该目录的套件），避免陈旧输出造成假差异。
namespace TypeScript.Harness;

using TypeScript.Harness.FourslashCases;
using TypeScript.Repo;
using TypeScript.TestRunner;
using TypeScript.TestUtil;
using TypeScript.TestUtil.Baseline;

internal static class FourslashSuite
{
    /// <summary>返回进程退出码（0 = 全绿）。</summary>
    public static int Run(string[] caseFilters, int limit, int offset, int maxDiffs, bool listOnly)
    {
        var all = FourslashCaseRegistry.All;
        var selected = new List<(string Name, Action<IHarnessTest> Body)>();
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

        Console.WriteLine($"[fourslash] registry = {all.Length} case(s)");
        Console.WriteLine($"[fourslash] selected = {selected.Count} case(s)");
        if (listOnly)
        {
            foreach (var (name, _) in selected)
            {
                Console.WriteLine("  " + name);
            }
            return 0;
        }
        if (selected.Count == 0)
        {
            Console.Error.WriteLine("no fourslash case matched --case filter(s): " + string.Join(", ", caseFilters));
            return 2;
        }

        var localFourslash = Path.Combine(Repo.TestDataPath(), "baselines", "local", "fourslash");
        if (Directory.Exists(localFourslash))
        {
            Directory.Delete(localFourslash, recursive: true);
        }

        var report = new RunReport();
        var only = caseFilters.Length > 0 || limit != int.MaxValue || offset != 0
            ? selected.Select(s => s.Name).ToHashSet(StringComparer.Ordinal)
            : null;
        var root = HarnessTest.Root(report, [], only);

        var trackingDir = Environment.GetEnvironmentVariable("TSGO_BASELINE_TRACKING_DIR") ?? "";
        if (trackingDir.Length > 0 && !Directory.Exists(trackingDir))
        {
            Directory.CreateDirectory(trackingDir);
        }

        HarnessProgress.Configure(0, selected.Count);
        var trackingCleanup = Baseline.Track();
        try
        {
            foreach (var (name, body) in selected)
            {
                root.Run(name, body);
            }
        }
        finally
        {
            trackingCleanup();
        }

        // ---- 基线比较 ----
        var reference = IndexReferenceBaselines();
        var identical = 0;
        var mismatch = 0;
        var missingReference = 0;
        var notGenerated = 0;
        var diffLines = new List<string>();
        var byCommand = new SortedDictionary<string, (int Ok, int Bad)>(StringComparer.Ordinal);
        var failedCases = new List<string>();

        foreach (var (name, _) in selected)
        {
            var caseBase = CaseBaseName(name);
            var refs = reference.TryGetValue(caseBase, out var list) ? list : [];
            var caseFailed = report.CaseFailed(name);
            if (caseFailed)
            {
                failedCases.Add(name);
            }
            if (refs.Count == 0)
            {
                // 有用例本身不产生 baseline（例如只做 VerifyCurrentLineContent 断言）：不算基线比对对象。
                if (caseFailed)
                {
                    missingReference++;
                }
                continue;
            }
            foreach (var relative in refs)
            {
                var command = relative.Split('/')[1];
                var fileName = relative[(relative.LastIndexOf('/') + 1)..];
                var localPath = Path.Combine(localFourslash, command, fileName);
                var referencePath = Path.Combine(Repo.TestDataPath(), "baselines", "reference", relative);
                var bucket = byCommand.TryGetValue(command, out var v) ? v : (Ok: 0, Bad: 0);
                if (File.Exists(localPath))
                {
                    mismatch++;
                    bucket.Bad++;
                    var diff = FirstDiff(referencePath, localPath);
                    if (diff is not null && diffLines.Count < maxDiffs)
                    {
                        diffLines.Add($"  DIFF [fourslash/{command}/{fileName}] {diff}");
                    }
                }
                else
                {
                    identical++;
                    bucket.Ok++;
                }
                byCommand[command] = bucket;
            }
        }

        Console.WriteLine();
        Console.WriteLine($"[fourslash] cases executed = {report.CasesRun.Count}, cases with failures = {failedCases.Count}");
        Console.WriteLine($"[fourslash] baselines: byte-identical = {identical}, mismatch = {mismatch}, " +
            $"cases-without-reference-baseline = {missingReference}, baselines-not-tracked = {notGenerated}");
        Console.WriteLine($"[fourslash] test failures = {report.Failures.Count}, skips = {report.Skips.Count}");
        Console.WriteLine();
        Console.WriteLine("[fourslash] by command (byte-identical / mismatch):");
        foreach (var (command, counts) in byCommand)
        {
            Console.WriteLine($"  {command,-32} {counts.Ok,5} / {counts.Bad,3}");
        }
        foreach (var line in diffLines)
        {
            Console.WriteLine(line);
        }
        if (failedCases.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("[fourslash] failed cases (first message):");
            foreach (var caseName in failedCases.Take(40))
            {
                var message = report.Failures
                    .Where(f => f.Name == caseName || f.Name.StartsWith(caseName + "/", StringComparison.Ordinal))
                    .Select(f => f.Message)
                    .FirstOrDefault() ?? "";
                Console.WriteLine($"  FAIL {caseName}: {FirstLine(message)}");
            }
        }
        if (report.Skips.Count > 0)
        {
            Console.WriteLine($"  ({report.Skips.Count} skipped subtest(s): 照抄 Go 的 t.Skip，见注册表用例)");
        }

        var ok = report.Failures.Count == 0 && mismatch == 0;
        Console.WriteLine();
        Console.WriteLine(ok ? "RESULT: PASS" : "RESULT: FAIL");
        return ok ? 0 : 1;
    }

    /// <summary>用例名（Go 顶层测试函数名）→ baseline 文件基名（Go 的 getBaseFileNameFromTest）。</summary>
    private static string CaseBaseName(string testName)
    {
        var name = testName.Split('/')[^1];
        if (name.StartsWith("Test", StringComparison.Ordinal))
        {
            name = name["Test".Length..];
        }
        name = TypeScript.StringUtil.StringUtil.LowerFirstChar(name);
        return name switch
        {
            "callHierarchyFunctionAmbiguity1" => "callHierarchyFunctionAmbiguity.1",
            "callHierarchyFunctionAmbiguity2" => "callHierarchyFunctionAmbiguity.2",
            "callHierarchyFunctionAmbiguity3" => "callHierarchyFunctionAmbiguity.3",
            "callHierarchyFunctionAmbiguity4" => "callHierarchyFunctionAmbiguity.4",
            "callHierarchyFunctionAmbiguity5" => "callHierarchyFunctionAmbiguity.5",
            _ => name,
        };
    }

    /// <summary>`baselines/reference/fourslash/&lt;command&gt;/&lt;case&gt;.&lt;ext&gt;` 按用例基名建索引。</summary>
    private static Dictionary<string, List<string>> IndexReferenceBaselines()
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var root = Path.Combine(Repo.TestDataPath(), "baselines", "reference", "fourslash");
        if (!Directory.Exists(root))
        {
            return result;
        }
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            var command = Path.GetFileName(dir);
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                var name = Path.GetFileName(file);
                var cut = name.IndexOfAny(['.', '(']);
                var stem = cut < 0 ? name : name[..cut];
                if (!result.TryGetValue(stem, out var list))
                {
                    list = [];
                    result[stem] = list;
                }
                list.Add($"fourslash/{command}/{name}");
            }
        }
        foreach (var list in result.Values)
        {
            list.Sort(StringComparer.Ordinal);
        }
        return result;
    }

    private static string? FirstDiff(string referencePath, string localPath)
    {
        var a = File.ReadAllLines(referencePath);
        var b = File.ReadAllLines(localPath);
        var n = Math.Max(a.Length, b.Length);
        for (var i = 0; i < n; i++)
        {
            var x = i < a.Length ? a[i] : "<missing>";
            var y = i < b.Length ? b[i] : "<missing>";
            if (x != y)
            {
                return $"line {i + 1}: expected <{Truncate(x)}> actual <{Truncate(y)}>";
            }
        }
        return null;
    }

    private static string Truncate(string s) => s.Length > 160 ? s[..160] + "…" : s;

    private static string FirstLine(string message)
    {
        var lines = message.Split('\n');
        return lines.Length <= 6 ? message : string.Join('\n', lines.Take(6)) + "\n    …";
    }
}
