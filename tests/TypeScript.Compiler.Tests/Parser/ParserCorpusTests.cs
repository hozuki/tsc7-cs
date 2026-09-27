// 真实语料级验证（C# 侧对应 Go 的 parser_test.go:FuzzParser —— 它把 fixtures 与
// tests/cases/{compiler,conformance} 当种子语料喂给 parser）。这里不是"手写几个片段"，而是把 TSGO 检出的
// 全部可解析文件都过一遍 Parser，断言四类可观察契约：
//   1) 不抛异常（异常消息 + 文件名 + unit 名一起带出来）；
//   2) 基本形状：Kind == SourceFile、EndOfFileToken/Statements 非空；
//   3) 位置不变量：每个顶层语句 Pos() <= End()，且 End() <= EndOfFileToken.End()；
//   4) 确定性：同一 unit 解析两次，语句数与诊断数一致。
// 另加"下界计数"断言：语料条数与语句总数不得低于实测值的一半左右，防止"一个文件都没走到"的假通过。
//
// 语料来源与选择规则（与 docs/harness.md §2、Go 的 FuzzParser 一致）：
//   * fixtures/**：按 tspath.AllSupportedExtensionsWithJson 那套扩展名挑可解析文件（.ts/.tsx/.d.ts/.js/.jsx/
//     .mjs/.cjs/.mts/.cts/.json + .d.*），整个文件 = 一个 unit；
//   * tests/cases/{compiler,conformance}/**：按 `// @filename:` 拆 unit（最简拆分器，见 SplitUnits），
//     unit 名带扩展名、扩展名不在上面那套里的 unit 跳过（Go 侧 f.Skip 的等价物）。
// 目录不存在时直接 return：这是"需要 TSGO 检出"的测试（TSGO_ROOT 指定，缺省 D:\Source\GitHub\TypeScript-Go）。
// 实测（本机 TSGO 检出，2026-09）：fixtures 166 unit/8561 语句、compiler 9211 unit/54895 语句、
// conformance 8046 unit/48404 语句，三个 suite 合计约 2 秒（含确定性复跑），远低于 60 秒目标，无需抽样。
using Stopwatch = System.Diagnostics.Stopwatch;
using System.Text;
using System.Text.RegularExpressions;
using TypeScript.Ast;
using TypeScript.Core;
using Xunit;
using Xunit.Abstractions;
using static TypeScript.Core.Core;
using static TypeScript.Parser.Parser;
using static TypeScript.TSPath.TSPath;
using Path = TypeScript.TSPath.Path;

namespace TypeScript.Tests;

public class ParserCorpusTests
{
    private readonly ITestOutputHelper _output;

    public ParserCorpusTests(ITestOutputHelper output) => _output = output;

    // docs/architecture.md §5：TSGO 仓库根目录由 TSGO_ROOT 指定，缺省探测 D:\Source\GitHub\TypeScript-Go。
    private static readonly string _tsgoRoot =
        Environment.GetEnvironmentVariable("TSGO_ROOT") is { Length: > 0 } root ? root : @"D:\Source\GitHub\TypeScript-Go";

    private static readonly string _testDataRoot = System.IO.Path.Combine(_tsgoRoot, "tsc", "testdata");

    // Go: testrunner 的指令正则 `^\/\/\s*@(\w+)\s*:\s*([^\r\n]*)` 里 `@filename` 那一支。
    private static readonly Regex _filenameDirective = new(@"^//\s*@filename\s*:\s*([^\r\n]*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // 指令行本身不是 unit 内容（Go 命中 optionRegex 后 continue）。
    private static readonly Regex _anyDirective = new(@"^//\s*@\w+\s*:", RegexOptions.Compiled);

    private static readonly HashSet<string> _supportedExtensions = [.. AllSupportedExtensionsWithJson.SelectMany(static x => x)];

    private sealed record CorpusUnit(string Source, string Name, string Text);

    [Fact]
    public void FixturesParseWithoutErrors()
    {
        if (!Directory.Exists(_testDataRoot))
        {
            return; // 需要 TSGO 检出（TSGO_ROOT），没有就跳过。
        }

        var units = new List<CorpusUnit>();
        foreach (var file in Walk(System.IO.Path.Combine(_testDataRoot, "fixtures")))
        {
            if (_supportedExtensions.Contains(TryGetExtensionFromPath(file)))
            {
                units.Add(new CorpusUnit(file, System.IO.Path.GetFileName(file), File.ReadAllText(file)));
            }
        }

        // 实测 166 个文件 / 8561 条顶层语句（下界放宽到一半左右，只拦"语料没走到"）。
        RunCorpus("fixtures", units, minUnits: 150, minStatements: 5000);
    }

    [Theory]
    [InlineData("compiler", 8000, 40000)]
    [InlineData("conformance", 7000, 35000)]
    public void CasesParseWithoutErrors(string suite, int minUnits, int minStatements)
    {
        if (!Directory.Exists(_testDataRoot))
        {
            return; // 需要 TSGO 检出（TSGO_ROOT），没有就跳过。
        }

        var units = new List<CorpusUnit>();
        foreach (var file in Walk(System.IO.Path.Combine(_testDataRoot, "tests", "cases", suite)))
        {
            foreach (var (name, text) in SplitUnits(File.ReadAllText(file), System.IO.Path.GetFileName(file)))
            {
                if (_supportedExtensions.Contains(TryGetExtensionFromPath(name)))
                {
                    units.Add(new CorpusUnit(file, name, text));
                }
            }
        }

        RunCorpus(suite, units, minUnits, minStatements);
    }

    private void RunCorpus(string suite, List<CorpusUnit> units, int minUnits, int minStatements)
    {
        var exceptions = new List<string>();
        var shapes = new List<string>();
        var positions = new List<string>();
        var nondeterminism = new List<string>();
        var statements = 0L;

        var stopwatch = Stopwatch.StartNew();
        foreach (var unit in units)
        {
            SourceFile first;
            try
            {
                first = ParseUnit(unit.Name, unit.Text);
            }
            catch (Exception ex)
            {
                exceptions.Add($"{unit.Source}|{unit.Name}: 抛异常 {ex.GetType().Name}: {FirstLine(ex.Message)}");
                continue;
            }

            if (first.Kind != Kind.SourceFile || first.EndOfFileToken is null || first.Statements is null)
            {
                shapes.Add(
                    $"{unit.Source}|{unit.Name}: kind={first.Kind} " +
                    $"endOfFileToken={(first.EndOfFileToken is null ? "null" : "非 null")} " +
                    $"statements={(first.Statements is null ? "null" : "非 null")}");
            }

            var endOfFileTokenEnd = first.EndOfFileToken?.End() ?? -1;
            foreach (var statement in first.Statements?.Nodes ?? [])
            {
                if (statement.Pos() > statement.End() || statement.End() > endOfFileTokenEnd)
                {
                    // 越界/负数是真 bug（scanner/parser 的前几轮就是被位置不变量抓出来的）：
                    // 合法的"缺失节点"是 Pos() == End() == -1（合成节点没有位置），仍然满足 Pos() <= End()。
                    positions.Add(
                        $"{unit.Source}|{unit.Name}: {statement.Kind} Pos()={statement.Pos()} End()={statement.End()} " +
                        $"(源码长度={unit.Text.Length}，EndOfFileToken.End()={endOfFileTokenEnd})");
                    break;
                }
            }

            statements += first.Statements!.Nodes!.Length;

            var second = ParseUnit(unit.Name, unit.Text);
            if (second.Statements!.Nodes!.Length != first.Statements!.Nodes!.Length ||
                second.Diagnostics().Length != first.Diagnostics().Length)
            {
                nondeterminism.Add(
                    $"{unit.Source}|{unit.Name}: 语句数 {first.Statements.Nodes.Length} -> {second.Statements.Nodes.Length}，" +
                    $"诊断数 {first.Diagnostics().Length} -> {second.Diagnostics().Length}");
            }
        }

        stopwatch.Stop();
        _output.WriteLine(
            $"{suite}: unit={units.Count} 顶层语句={statements} 耗时={stopwatch.ElapsedMilliseconds} ms " +
            $"异常={exceptions.Count} 形状={shapes.Count} 位置={positions.Count} 确定性={nondeterminism.Count}");

        Assert.True(units.Count >= minUnits, $"{suite}: 语料 unit 数 {units.Count} < 下界 {minUnits}（语料没走到？）");
        Assert.True(statements >= minStatements, $"{suite}: 顶层语句数 {statements} < 下界 {minStatements}（解析出空 SourceFile？）");
        Assert.True(exceptions.Count == 0, $"{suite}: {exceptions.Count} 个 unit 解析抛异常：\n{Join(exceptions)}");
        Assert.True(shapes.Count == 0, $"{suite}: {shapes.Count} 个 unit 的 SourceFile 形状不对：\n{Join(shapes)}");
        Assert.True(positions.Count == 0, $"{suite}: {positions.Count} 个 unit 违反位置不变量：\n{Join(positions)}");
        Assert.True(nondeterminism.Count == 0, $"{suite}: {nondeterminism.Count} 个 unit 解析不确定：\n{Join(nondeterminism)}");
    }

    // 与 Go 的 FuzzParser 同一形状：文件名 "/index" + 扩展名，scriptKind 由扩展名推。
    private static SourceFile ParseUnit(string name, string text)
    {
        var fileName = "/index" + TryGetExtensionFromPath(name);
        var opts = new SourceFileParseOptions { FileName = fileName, Path = new Path(fileName) };
        return ParseSourceFile(opts, text, GetScriptKindFromFileName(fileName));
    }

    private static IEnumerable<string> Walk(string root) =>
        Directory.Exists(root) ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories) : [];

    /// <summary>
    /// docs/harness.md §2 的最简拆分器（完整版要等 tsoptions，本轮只需要 unit 边界）：
    /// `// @filename: x.ts` 起新 unit；没有该指令时整份内容是一个 unit，名字取用例文件 basename；
    /// 内容按行拼回 `\n`（内存里一律 LF）。与 Go 的 test_case_parser.go 的差异只有两处，都不影响本轮语义：
    /// (a) 首个 `@filename` 之前的内容被丢弃（完整 harness 在它不是纯注释时 panic，真实语料里只可能是选项注释）；
    /// (b) 首个内容行之前的空行被丢掉（Go 用 `if currentFileContent.Len() != 0` 补 `\n`，行为相同）。
    /// </summary>
    private static List<(string Name, string Text)> SplitUnits(string text, string fileName)
    {
        var units = new List<(string, string)>();
        var content = new StringBuilder();
        var currentFileName = "";
        var sawDirective = false;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.EndsWith('\r') ? rawLine[..^1] : rawLine;
            if (_filenameDirective.Match(line) is { Success: true } match)
            {
                if (currentFileName.Length != 0)
                {
                    units.Add((currentFileName, content.ToString()));
                }

                content.Clear();
                currentFileName = match.Groups[1].Value.Trim();
                sawDirective = true;
                continue;
            }

            if (_anyDirective.IsMatch(line))
            {
                continue; // 指令行不是内容
            }

            if (content.Length != 0)
            {
                content.Append('\n');
            }

            content.Append(line);
        }

        if (!sawDirective && currentFileName.Length == 0)
        {
            currentFileName = GetBaseFileName(fileName);
        }

        units.Add((currentFileName, content.ToString()));
        return units;
    }

    private static string Join(List<string> failures)
    {
        var builder = new StringBuilder();
        foreach (var failure in failures.Take(10))
        {
            builder.Append("  - ").Append(failure).Append('\n');
        }

        if (failures.Count > 10)
        {
            builder.Append($"  …（共 {failures.Count} 条）");
        }

        return builder.ToString();
    }

    private static string FirstLine(string message)
    {
        var index = message.IndexOf('\n');
        return index < 0 ? message : message[..index];
    }
}
