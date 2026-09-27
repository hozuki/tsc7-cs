// ported from: internal/testrunner/transpile_runner.go
//
// 与 Go 的差异（逐条）：
// 1) 包级 var/func（transpileBaselineRegex/transpileVaryBy/formatTranspileConfigurationName/
//    appendTranspileSection/cleanTranspileBaselines/RunTranspileTests/NewTranspileBaselineRunner）落在静态类
//    `TestRunner`（§2 的 partial 约定；`localBasePath` 与 compiler_runner.go 共用 CompilerRunner.cs 里的 `_localBasePath`）。
// 2) `*testing.T` → `IHarnessTest`；`t.Context()` → `t.CancellationToken`（TranspileModule/TranspileDeclaration 收它）。
// 3) `transpile.Options`/`transpile.Output`/`TranspileModule`/`TranspileDeclaration` → `TypeScript.Transpile`
//    （internal/transpile 已随本波落地，见 Transpile/Transpile.cs）。
// 4) `diagnosticwriter.WrapASTDiagnostics`/`CompareASTDiagnostics`：C# 侧 `TypeScript.Ast.Diagnostic` 本身就是
//    Go 里 `diagnosticwriter.Diagnostic` 接口的唯一实现（Go 的包装/比较器是为泛型复用而设），所以这里直接传
//    `output.Diagnostics` 给 `TsBaseline.GetErrorBaseline`（它内部走同一套 `compareDiagnostics`），不开新包。
// 5) `strings.TrimSuffix(baseName, extension)` → `EndsWith` + 切片（Go 的 TrimSuffix 在 suffix 为空时返回原串）。
namespace TypeScript.TestRunner;

using System.Text;
using System.Text.RegularExpressions;
using TypeScript.Core;
using TypeScript.TestUtil;
using TypeScript.TestUtil.Baseline;
using TypeScript.TestUtil.HarnessUtil;
using TypeScript.TestUtil.TsBaseline;
using TypeScript.OutputPaths;
using TypeScript.Transpile;
using static TypeScript.Transpile.Transpile;
using TypeScript.Vfs.OsVfs;
using static TypeScript.StringUtil.StringUtil;
using static TypeScript.TSPath.TSPath;

/// <summary>Go 的 <c>testrunner</c> 包级声明（transpile_runner.go）。</summary>
public static partial class TestRunner
{
    /// <summary>Go: <c>var transpileBaselineRegex = regexp.MustCompile(`\.[cm]?[tj]sx?$`)</c>（transpile_runner.go:22）。</summary>
    internal static readonly Regex _transpileBaselineRegex = new(@"\.[cm]?[tj]sx?$", RegexOptions.CultureInvariant);

    /// <summary>Go: <c>var transpileVaryBy = map[string]struct{}{...}</c>（transpile_runner.go:24-28）。</summary>
    internal static readonly HashSet<string> _transpileVaryBy = ["declarationmap", "sourcemap", "inlinesourcemap"];

    /// <summary>Go: <c>func NewTranspileBaselineRunner() *TranspileBaselineRunner</c>（transpile_runner.go:37-41）。</summary>
    public static TranspileBaselineRunner NewTranspileBaselineRunner() => new();

    /// <summary>Go: <c>func formatTranspileConfigurationName(name string) string</c>（transpile_runner.go:97-101）。</summary>
    internal static string FormatTranspileConfigurationName(string name)
    {
        name = name.Replace("declarationmap=", "declarationMap=", StringComparison.Ordinal);
        name = name.Replace("inlinesourcemap=", "inlineSourceMap=", StringComparison.Ordinal);
        return name.Replace("sourcemap=", "sourceMap=", StringComparison.Ordinal);
    }

    /// <summary>Go: <c>func appendTranspileSection(result *strings.Builder, fileName string, content string)</c>（transpile_runner.go:170-176）。</summary>
    internal static void AppendTranspileSection(StringBuilder result, string fileName, string content)
    {
        result.Append($"//// [{fileName}] ////\r\n");
        result.Append(content);
        if (!content.EndsWith('\n'))
        {
            result.Append("\r\n");
        }
    }

    /// <summary>Go: <c>func cleanTranspileBaselines()</c>（transpile_runner.go:178-182）。</summary>
    internal static void CleanTranspileBaselines()
    {
        var path = Path.Combine(_localBasePath, "transpile");
        try
        {
            // Go: os.RemoveAll(...)（不存在时返回 nil）
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch (Exception ex)
        {
            // Go: panic("Could not clean up transpile baselines: " + err.Error())
            Debug.Fail("Could not clean up transpile baselines: " + ex.Message);
        }
    }

    /// <summary>Go: <c>func RunTranspileTests(t *testing.T)</c>（transpile_runner.go:184-187）。</summary>
    public static void RunTranspileTests(IHarnessTest t)
    {
        CleanTranspileBaselines();
        NewTranspileBaselineRunner().RunTests(t);
    }
}

/// <summary>Go: <c>type TranspileBaselineRunner struct</c>（transpile_runner.go:30-33）。</summary>
public sealed class TranspileBaselineRunner : Runner
{
    private string[] _testFiles = [];
    private readonly string _basePath;

    /// <summary>Go: <c>func NewTranspileBaselineRunner() *TranspileBaselineRunner</c>（transpile_runner.go:37-41）。</summary>
    public TranspileBaselineRunner()
    {
        _basePath = "../testdata/tests/cases/transpile";
    }

    /// <summary>Go: <c>func (r *TranspileBaselineRunner) EnumerateTestFiles() []string</c>（transpile_runner.go:43-53）。</summary>
    public string[] EnumerateTestFiles()
    {
        if (_testFiles.Length > 0)
        {
            return _testFiles;
        }

        string[] files;
        try
        {
            files = HarnessUtil.EnumerateFiles(_basePath, TestRunner._transpileBaselineRegex, true /*recursive*/);
        }
        catch (Exception ex)
        {
            // Go: panic("Could not read transpile test files: " + err.Error())
            Debug.Fail("Could not read transpile test files: " + ex.Message);
            throw;
        }
        _testFiles = files;
        return files;
    }

    /// <summary>Go: <c>func (r *TranspileBaselineRunner) RunTests(t *testing.T)</c>（transpile_runner.go:55-59）。</summary>
    public void RunTests(IHarnessTest t)
    {
        foreach (var fileName in EnumerateTestFiles())
        {
            RunTest(t, fileName);
        }
    }

    /// <summary>Go: <c>func (r *TranspileBaselineRunner) runTest(t *testing.T, fileName string)</c>（transpile_runner.go:61-95）。</summary>
    private void RunTest(IHarnessTest t, string fileName)
    {
        if (!Osvfs.FS().TryReadFile(fileName, out var content))
        {
            // Go: panic("Could not read transpile test file: " + fileName)
            Debug.Fail("Could not read transpile test file: " + fileName);
        }
        var settings = TestRunner.ExtractCompilerSettings(content);
        var configurations = HarnessUtil.GetFileBasedTestConfigurations(t, settings, TestRunner._transpileVaryBy);
        if (configurations.Count == 0)
        {
            configurations = [new NamedTestConfiguration { Config = settings }];
        }

        var extension = GetAnyExtensionFromPath(fileName, [], false);
        var baseName = GetBaseFileName(fileName);
        var justName = baseName.EndsWith(extension, StringComparison.Ordinal) && extension.Length > 0
            ? baseName[..^extension.Length]
            : baseName;
        var units = TestRunner.MakeUnitsFromTest(content, baseName)._testUnitData;

        foreach (var configuration in configurations)
        {
            var configuredName = justName;
            if (configuration.Name != "")
            {
                configuredName += "(" + TestRunner.FormatTranspileConfigurationName(configuration.Name) + ")";
            }
            var capturedConfiguredName = configuredName;
            var capturedConfiguration = configuration;
            t.Run(configuredName, t2 =>
            {
                var options = new CompilerOptions();
                var harnessOptions = new HarnessOptions();
                HarnessUtil.SetOptionsFromTestConfig(t2, capturedConfiguration.Config, options, harnessOptions, TestRunner.SrcFolder, false);

                if (!options.EmitDeclarationOnly.IsTrue())
                {
                    RunKind(t2, capturedConfiguredName, extension, units, options, harnessOptions, false);
                }
                if (options.Declaration.IsTrue())
                {
                    RunKind(t2, capturedConfiguredName, extension, units, options, harnessOptions, true);
                }
            });
        }
    }

    /// <summary>Go: <c>func (r *TranspileBaselineRunner) runKind(...)</c>（transpile_runner.go:103-168）。</summary>
    private static void RunKind(
        IHarnessTest t,
        string configuredName,
        string extension,
        TestUnit[] units,
        CompilerOptions options,
        HarnessOptions harnessOptions,
        bool declaration)
    {
        var result = new StringBuilder();
        foreach (var unit in units)
        {
            TestRunner.AppendTranspileSection(result, unit._name, unit._content);
        }

        foreach (var unit in units)
        {
            var transpileOptions = new Options
            {
                CompilerOptions = options,
                FileName = unit._name,
                ReportDiagnostics = harnessOptions.ReportDiagnostics,
            };
            Output? output;
            if (declaration)
            {
                output = TranspileDeclaration(t.CancellationToken, unit._content, transpileOptions);
            }
            else
            {
                output = TranspileModule(t.CancellationToken, unit._content, transpileOptions);
            }
            if (output is null)
            {
                // Go: t.Fatal("transpilation was canceled")
                t.Fatal("transpilation was canceled");
            }

            var outputExtension = OutputPaths.GetOutputExtension(unit._name, options.Jsx);
            if (declaration)
            {
                outputExtension = GetDeclarationEmitExtensionForPath(unit._name);
            }
            var outputFileName = ChangeExtension(unit._name, outputExtension);
            TestRunner.AppendTranspileSection(result, outputFileName, output.OutputText);
            if (output.SourceMapText != "")
            {
                TestRunner.AppendTranspileSection(result, outputFileName + ".map", output.SourceMapText);
            }
            if (output.Diagnostics.Length > 0)
            {
                result.Append("\r\n\r\n//// [Diagnostics reported]\r\n");
                var diagnosticFileName = unit._name;
                var file = output.Diagnostics[0].File();
                if (file is not null)
                {
                    diagnosticFileName = file.FileName();
                }
                var errorBaseline = TsBaseline.GetErrorBaseline(
                    t,
                    [new TestFile { UnitName = diagnosticFileName, Content = unit._content }],
                    output.Diagnostics,
                    options.Pretty.IsTrue());
                result.Append(errorBaseline.Replace(diagnosticFileName, unit._name, StringComparison.Ordinal));
                if (!result.ToString().EndsWith('\n'))
                {
                    result.Append("\r\n");
                }
            }
        }

        var baselineExtension = OutputPaths.GetOutputExtension(configuredName + extension, options.Jsx);
        if (declaration)
        {
            baselineExtension = GetDeclarationEmitExtensionForPath(configuredName + extension);
        }
        var baselineName = configuredName + baselineExtension;
        Baseline.Run(t, "transpile/" + baselineName, result.ToString(), new BaselineOptions());
    }
}
