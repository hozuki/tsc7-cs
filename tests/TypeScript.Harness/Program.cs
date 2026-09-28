// M7 验收 harness 的可独立运行入口：枚举 TSGO 用例 →（runner 内部）编译 + 生成 baseline →
// 与 `TSGO_ROOT/tsc/testdata/baselines/reference/**` 逐字节比较，打印通过/失败与首个差异。
//
// Go 侧对应入口：internal/testrunner/compiler_runner_test.go 的 `runCompilerTests`
// （两个 runner：TestTypeRegression = suite `compiler`、TestTypeConformance = suite `conformance`，
//  并断言两个 suite 的用例 basename 不重名）与 transpile_runner_test.go 的 `RunTranspileTests`。
//
// 比较口径（重要，与 Go 的行为一致）：`baseline.Run` 只在**生成内容与 reference 不等**时才写
// `baselines/local/**` 并报错；相等时两者都提前返回（Go 的 writeComparison 同样如此）。所以"local 里没有
// 该文件 + 该 baseline 被生成过 + 没有失败" == **逐字节一致**。"生成过"用 Go 的 baseline tracking
// 机制取证：设 TSGO_BASELINE_TRACKING_DIR 后 `Baseline.Track()` 会把每个写过的 baseline 相对路径
// 落盘（TestMain.cs 的 recordBaseline/writeRecordedBaselines），本次运行据此得到"实际生成并比较过的清单"。
namespace TypeScript.Harness;

using System.Globalization;
using System.Text;
using TypeScript.Repo;
using TypeScript.TestRunner;
using TypeScript.TestUtil;
using TypeScript.TestUtil.Baseline;
using static TypeScript.TSPath.TSPath;

internal static class Program
{
    private static int Main(string[] args)
    {
        var suites = new List<string>();
        var cases = new List<string>();
        var limit = int.MaxValue;
        var maxDiffs = 10;
        var listOnly = false;
        var caseTimeoutSeconds = 0;
        var offset = 0;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--suite":
                    suites.Add(args[++i]);
                    break;
                case "--case":
                    // 允许 `--case a.ts,b.ts` 逗号分隔（与 TSGO/tsc 命令行习惯一致）；同时保留重复 `--case` 的写法。
                    foreach (var part in args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        cases.Add(part);
                    }

                    break;
                case "--limit":
                    limit = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--offset":
                    offset = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--diff":
                    maxDiffs = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--list":
                    listOnly = true;
                    break;
                case "--case-timeout":
                    // 0（缺省）表示不启用，与 Go harness 行为一致；>0 时单用例超时即中止整轮（同 `go test -timeout` 语义）。
                    caseTimeoutSeconds = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                default:
                    Console.Error.WriteLine($"unknown argument: {args[i]}");
                    return 2;
            }
        }
        if (suites.Count == 0)
        {
            suites.Add("all");
        }
        if (suites.Remove("all"))
        {
            suites.InsertRange(0, ["compiler", "conformance", "transpile"]);
        }

        var trackingDir = Environment.GetEnvironmentVariable("TSGO_BASELINE_TRACKING_DIR") ?? "";
        if (trackingDir.Length > 0 && !Directory.Exists(trackingDir))
        {
            // 健壮性（非 Go 原逻辑）：Go 侧 tracking 目录由测试基建保证存在；C# 入口自己建，避免首次运行
            // 因为"目录不存在"而在写 tracking 文件时才炸（现象是一次难看的 IOException）。
            Directory.CreateDirectory(trackingDir);
        }

        // fourslash 不是「文件型」套件（用例是代码，不是 testdata 里的文件），单独走一条路径：
        // 见 tests/TypeScript.Harness/FourslashSuite.cs（P6 用例转写 + 对拍）。
        if (suites.Remove("fourslash"))
        {
            var fsExit = FourslashSuite.Run([.. cases], limit, offset, maxDiffs, listOnly);
            if (suites.Count == 0)
            {
                return fsExit;
            }
        }

        Console.WriteLine($"TSGO_ROOT       = {Repo.RootPath()}");
        Console.WriteLine($"TestDataPath    = {Repo.TestDataPath()}");
        Console.WriteLine($"suites          = {string.Join(", ", suites)}");
        Console.WriteLine($"case filters    = {(cases.Count == 0 ? "<none>" : string.Join(", ", cases))}");
        Console.WriteLine($"limit           = {(limit == int.MaxValue ? "<none>" : limit.ToString(CultureInfo.InvariantCulture))}");
        Console.WriteLine($"offset          = {offset}");
        Console.WriteLine($"baseline track  = {(trackingDir.Length == 0 ? "<off>" : trackingDir)}");
        Console.WriteLine();

        var report = new RunReport();

        var selectedBySuite = new Dictionary<string, List<string>>();
        foreach (var suite in suites)
        {
            var files = SelectCases(suite, cases, limit, offset);
            selectedBySuite[suite] = files;
            Console.WriteLine($"[{suite}] selected {files.Count} case(s)");
        }

        AssertUniqueBasenames(selectedBySuite);

        // 分批/选择语义：`--limit/--offset` 与 `--case` 都必须同时约束"执行"（否则 runner 会跑全量用例）。
        var only = cases.Count > 0
            ? null
            : limit == int.MaxValue && offset == 0
                ? null
                : selectedBySuite.Values.SelectMany(v => v).Select(GetBaseFileName).ToHashSet(StringComparer.Ordinal);
        var root = HarnessTest.Root(report, [.. cases], only);

        // 用法护栏：显式给了 `--case` 却一个都没选中 → 明确报错退出（否则会被当成"全跑但全被过滤"而给出令人误解的 FAIL）。
        if (cases.Count > 0 && selectedBySuite.Values.Sum(v => v.Count) == 0)
        {
            Console.Error.WriteLine($"no test case matched --case filter(s): {string.Join(", ", cases)}");
            return 2;
        }

        // 进度与单用例超时看门狗（仅集成观测；--case-timeout 0 表示不启用）。
        HarnessProgress.Configure(caseTimeoutSeconds, selectedBySuite.Sum(kv => kv.Value.Count));

        // Go: TestMain 里的 `defer baseline.Track()()`。
        var trackingCleanup = Baseline.Track();
        try
        {
            foreach (var suite in suites)
            {
                var runner = CreateRunner(suite);
                Console.WriteLine($"[{suite}] running…");
                runner.RunTests(root);
            }
        }
        finally
        {
            trackingCleanup();
        }

        var recorded = ReadRecordedBaselines(trackingDir);
        if (recorded.Count > 0)
        {
            Console.WriteLine($"baseline tracking: {recorded.Count} baseline(s) generated and compared");
        }

        Console.WriteLine();
        var totalCases = 0;
        var totalExpected = 0;
        var totalIdentical = 0;
        foreach (var suite in suites)
        {
            var (expected, generated, identical, mismatch, missingLocal, extra, notGenerated, diffLines) =
                CompareSuite(suite, selectedBySuite[suite], maxDiffs, recorded);
            totalCases += selectedBySuite[suite].Count;
            totalExpected += expected;
            totalIdentical += identical;
            Console.WriteLine($"[{suite}] baselines: expected={expected} generated={generated} " +
                $"byte-identical={identical} mismatch={mismatch} not-generated={notGenerated} " +
                $"no-local-but-mismatch={missingLocal} extra-local={extra}");
            foreach (var line in diffLines)
            {
                Console.WriteLine(line);
            }
        }

        Console.WriteLine();
        var failedCases = 0;
        foreach (var caseName in report.CasesRun)
        {
            if (report.CaseFailed(caseName))
            {
                failedCases++;
            }
        }
        Console.WriteLine($"cases selected = {totalCases}, cases executed = {report.CasesRun.Count}, cases with failures = {failedCases}");
        Console.WriteLine($"baselines expected = {totalExpected}, byte-identical = {totalIdentical}");
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
        if (report.Skips.Count > 0)
        {
            Console.WriteLine("skipped subtests:");
            foreach (var (name, message) in report.Skips)
            {
                Console.WriteLine($"  SKIP {name}: {FirstLine(message)}");
            }
        }

        var ok = report.Failures.Count == 0 && totalIdentical == totalExpected && totalExpected > 0;
        Console.WriteLine(ok ? "RESULT: PASS" : "RESULT: FAIL");
        return ok ? 0 : 1;
    }

    private static Runner CreateRunner(string suite) => suite switch
    {
        "compiler" => TestRunner.NewCompilerBaselineRunner(CompilerTestType.Regression),
        "conformance" => TestRunner.NewCompilerBaselineRunner(CompilerTestType.Conformance),
        "transpile" => TestRunner.NewTranspileBaselineRunner(),
        _ => throw new ArgumentException("unknown suite: " + suite),
    };

    /// <summary>按 `--case`/`--limit`/`--offset` 选用例；skippedTests（Go compiler_runner.go:78-124）不参与选择
    /// （Go 的 runner 也会跳过它们，选进来只会让 expected baseline 变成假缺失）。
    /// `--offset` 只用于**分批跑**（跳过前 N 个已选中的用例），不属于 Go 原逻辑。</summary>
    private static List<string> SelectCases(string suite, List<string> cases, int limit, int offset)
    {
        var runner = CreateRunner(suite);
        var selected = new List<string>();
        var matched = 0;
        foreach (var file in runner.EnumerateTestFiles())
        {
            var basename = GetBaseFileName(file);
            if (suite != "transpile" && TestRunner.IsSkippedTest(basename))
            {
                continue;
            }
            if (cases.Count > 0 && !cases.Any(c => basename.Contains(c, StringComparison.Ordinal)))
            {
                continue;
            }
            if (matched++ < offset)
            {
                continue;
            }
            selected.Add(file);
            if (selected.Count >= limit)
            {
                break;
            }
        }
        return selected;
    }

    /// <summary>Go: compiler_runner_test.go:23-31 的 basename 唯一性断言（compiler/conformance 共用一个扁平 baseline 目录）。</summary>
    private static void AssertUniqueBasenames(Dictionary<string, List<string>> selectedBySuite)
    {
        var seen = new HashSet<string>();
        foreach (var suite in new[] { "compiler", "conformance" })
        {
            if (!selectedBySuite.TryGetValue(suite, out var files))
            {
                continue;
            }
            foreach (var file in files)
            {
                var basename = GetBaseFileName(file);
                if (!seen.Add(basename))
                {
                    Console.WriteLine($"DUPLICATE TEST FILE: {basename}");
                }
            }
        }
    }

    /// <summary>读 baseline tracking 落盘的清单（相对 baselines/reference 的路径，形如 `compiler/x.js`）。</summary>
    private static HashSet<string> ReadRecordedBaselines(string trackingDir)
    {
        var recorded = new HashSet<string>(StringComparer.Ordinal);
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

    /// <summary>把本次选中的用例映射到它们期望的 reference baseline 文件名，再与 local 逐字节比较。</summary>
    private static (int Expected, int Generated, int Identical, int Mismatch, int MissingLocal, int Extra, int NotGenerated, List<string> DiffLines)
        CompareSuite(string suite, List<string> selectedFiles, int maxDiffs, HashSet<string> recorded)
    {
        var referenceDir = Path.Combine(Repo.TestDataPath(), "baselines", "reference", suite);
        var localDir = Path.Combine(Repo.TestDataPath(), "baselines", "local", suite);
        var diffLines = new List<string>();
        if (!Directory.Exists(referenceDir) || selectedFiles.Count == 0)
        {
            return (0, 0, 0, 0, 0, 0, 0, diffLines);
        }

        // reference 文件按 "stem"（'.' 或 '(' 之前的部分）建索引，避免对着 23k 个文件反复扫描。
        var byStem = new Dictionary<string, List<string>>();
        foreach (var path in Directory.EnumerateFiles(referenceDir))
        {
            var name = Path.GetFileName(path);
            var cut = name.IndexOfAny(['.', '(']);
            var stem = cut < 0 ? name : name[..cut];
            if (!byStem.TryGetValue(stem, out var list))
            {
                list = [];
                byStem[stem] = list;
            }
            list.Add(name);
        }

        var expected = new List<string>();
        foreach (var file in selectedFiles)
        {
            var basename = GetBaseFileName(file);
            var extension = GetAnyExtensionFromPath(basename, [], false);
            var stem = extension.Length > 0 ? basename[..^extension.Length] : basename;
            if (byStem.TryGetValue(stem, out var list))
            {
                expected.AddRange(list);
            }
        }
        expected.Sort(StringComparer.Ordinal);

        var identical = 0;
        var mismatch = 0;
        var missingLocal = 0;
        var notGenerated = 0;
        var compared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in expected)
        {
            var referencePath = Path.Combine(referenceDir, name);
            var localPath = Path.Combine(localDir, name);
            var relative = suite + "/" + name;
            compared.Add(name);

            var isGenerated = recorded.Count == 0 || recorded.Contains(relative);
            if (!isGenerated)
            {
                notGenerated++;
                if (diffLines.Count < maxDiffs)
                {
                    diffLines.Add($"  NOT-GENERATED [{relative}] 该 baseline 没有被生成/比较过（baseline tracking 里没有它）");
                }
                continue;
            }

            if (File.Exists(localPath))
            {
                var diff = FirstDiff(referencePath, localPath);
                if (diff is null)
                {
                    identical++;
                }
                else
                {
                    mismatch++;
                    if (diffLines.Count < maxDiffs)
                    {
                        diffLines.Add($"  DIFF [{relative}] {diff}");
                    }
                }
            }
            else if (File.Exists(localPath + ".delete"))
            {
                mismatch++;
                if (diffLines.Count < maxDiffs)
                {
                    diffLines.Add($"  DIFF [{relative}] local 写了 `<no content>`（{name}.delete），但 reference 有内容");
                }
            }
            else
            {
                // Go 的 baseline.Run 在"生成内容 == reference"时不写 local、也不报错（writeComparison 的提前返回）。
                identical++;
            }
        }

        var extra = 0;
        if (Directory.Exists(localDir))
        {
            foreach (var path in Directory.EnumerateFiles(localDir))
            {
                var name = Path.GetFileName(path);
                if (name.EndsWith(".delete", StringComparison.Ordinal))
                {
                    continue;
                }
                if (!compared.Contains(name))
                {
                    extra++;
                }
            }
        }

        var generated = recorded.Count == 0 ? expected.Count : expected.Count(e => recorded.Contains(suite + "/" + e));
        return (expected.Count, generated, identical, mismatch, missingLocal, extra, notGenerated, diffLines);
    }

    /// <summary>逐字节比较，返回首个差异的描述（行号 1 基 + 字节偏移 + 十六进制），一致返回 null。</summary>
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
                var expectedByte = reference[i];
                var actualByte = local[i];
                var context = LineAt(reference, i);
                return $"first diff at line {line} (byte offset {i}): " +
                    $"expected 0x{expectedByte:X2} ({Escape(expectedByte)}) vs actual 0x{actualByte:X2} ({Escape(actualByte)}); " +
                    $"reference line: {context}";
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
        var text = Encoding.UTF8.GetString(bytes, start, end - start);
        return "\"" + (text.Length > 120 ? text[..120] + "…" : text) + "\"";
    }

    private static string Escape(byte b) => b switch
    {
        (byte)'\r' => "CR",
        (byte)'\n' => "LF",
        (byte)'\t' => "TAB",
        _ => b is >= 32 and < 127 ? ((char)b).ToString() : "·",
    };

    private static string FirstLine(string message)
    {
        var index = message.IndexOf('\n');
        return index < 0 ? message : message[..index];
    }

    /// <summary>失败详情：单行化 + 截断（先给足够信息定位，再防止刷屏）。</summary>
    private static string Detail(string message)
    {
        var single = message.Replace("\r\n", " | ", StringComparison.Ordinal).Replace('\n', ' ');
        return single.Length > 700 ? single[..700] + "…" : single;
    }
}
