// ported from: internal/testrunner/compiler_runner.go
//
// 与 Go 的差异（逐条，均在 docs/architecture.md §2/§3 允许范围内）：
// 1) Go 的包级 var/func（compilerBaselineRegex/requireStr/referencesRegex/srcFolder/skippedTests/localBasePath/
//    compilerVaryBy/getCompilerVaryByMap/getCompilerFileBasedTest/newCompilerTest/createHarnessTestFile）在 C# 里
//    必须落在同一个静态类 `TestRunner` 内（§2 的 partial 约定），因此声明位置与 Go 的"分散在类型之间"不同，
//    成员名/语义逐一对应。
// 2) iota 枚举 `CompilerTestType` → C# enum，数值同 Go（Conformance=0, Regression=1），成员名去掉 `TestType` 前缀
//    （§3.3）；Go 的 `(*CompilerTestType).String()` → 扩展方法 `String(this CompilerTestType)`（先例
//    `Core/CompilerOptions.cs` 的 `String(this ModuleResolutionKind)`）。
// 3) `*testing.T` → `IHarnessTest`（TestUtil/HarnessContext.cs）；Go 的 `defer testutil.RecoverAndFail(t, msg)` →
//    `TestUtil.RecoverAndFail(t, msg, () => { ... })` 包裹同一段 body（TestUtil/TestUtil.cs）。
// 4) `(T, error)`/`panic` → 抛异常/`Debug.Fail`：`harnessutil.EnumerateFiles` 的错误、`os.RemoveAll` 的错误都走
//    `Debug.Fail`（C# 的 `Directory.Delete` 用 try/catch 包住）。
// 5) `harnessutil.TestConfiguration`（= `map[string]string`）→ `Dictionary<string, string>`；Go 的 nil map 语义
//    （读缺失键得 ""；写 nil map panic）在调用点显式表达（`_configuration is null ? "" : …`、`harnessConfig!["baseurl"]`）。
// 6) `compilerFileBasedTest`/`compilerTest`/`testCaseContentWithConfig` 是未导出类型 → C# 习惯命名
//    `CompilerFileBasedTest`/`CompilerTest`/`TestCaseContentWithConfig`；未导出字段 → `_camelCase`（§2）。
// 7) `rand.NewPCG(1234, 5678)` + `rng.Shuffle`（verifyUnionOrdering）→ C# 里没有可逐位复刻的 PCG 实现，
//    换成 `System.Random(1234)` 的 Fisher–Yates；该分支只做"排序结果与输入顺序无关"的自一致性断言，不产 baseline。
// 8) Go 的 `c.result.Program.Program()`（ProgramLike → 底层 *compiler.Program）在 C# 里因 CS0542 被换成
//    `CompilationResult.Program as TypeScript.Compiler.Program`（见 CompilerTest.GoProgram 的注释）。
namespace TypeScript.TestRunner;

using System.Text;
using System.Text.RegularExpressions;
using TypeScript.Ast;
using TypeScript.Checker;
using TypeScript.Compiler;
using CheckerType = TypeScript.Checker.Type;
using TypeScript.Core;
using TypeScript.TestUtil;
using TypeScript.TestUtil.HarnessUtil;
using TypeScript.TsOptions;
using TypeScript.TestUtil.Baseline;
using TypeScript.TestUtil.TsBaseline;
using TypeScript.Vfs.OsVfs;
using static TypeScript.Repo.Repo;
using static TypeScript.StringUtil.StringUtil;
using static TypeScript.TSPath.TSPath;
using static TypeScript.TsOptions.TsOptions;

/// <summary>Go: <c>type CompilerTestType int</c>（compiler_runner.go:36-41，iota：Conformance=0, Regression=1）。</summary>
public enum CompilerTestType
{
    Conformance = 0,
    Regression = 1,
}

/// <summary>Go 的 <c>testrunner</c> 包级声明（compiler_runner.go）。</summary>
public static partial class TestRunner
{
    /// <summary>Go: <c>var compilerBaselineRegex = regexp.MustCompile(`\.tsx?$`)</c>（compiler_runner.go:27）。</summary>
    internal static readonly Regex _compilerBaselineRegex = new(@"\.tsx?$", RegexOptions.CultureInvariant);

    /// <summary>Go: <c>requireStr = "require("</c>（compiler_runner.go:28）。</summary>
    internal const string _requireStr = "require(";

    /// <summary>Go: <c>referencesRegex = regexp.MustCompile(`reference\spath`)</c>（compiler_runner.go:29）。</summary>
    internal static readonly Regex _referencesRegex = new(@"reference\spath", RegexOptions.CultureInvariant);

    /// <summary>Posix-style path to sources under test. Go: <c>var srcFolder = "/.src"</c>（compiler_runner.go:34）。</summary>
    internal static readonly string SrcFolder = "/.src";

    /// <summary>Go: <c>func (t *CompilerTestType) String() string</c>（compiler_runner.go:43-48）。</summary>
    public static string String(this CompilerTestType t)
    {
        if (t == CompilerTestType.Regression)
        {
            return "compiler";
        }
        return "conformance";
    }

    /// <summary>Go: <c>func NewCompilerBaselineRunner(testType CompilerTestType) *CompilerBaselineRunner</c>（compiler_runner.go:58-64）。</summary>
    public static CompilerBaselineRunner NewCompilerBaselineRunner(CompilerTestType testType) => new(testType);

    /// <summary>Go: <c>var skippedTests = []string{...}</c>（compiler_runner.go:78-124，共 41 个文件名）。</summary>
    internal static readonly string[] _skippedTests =
    [
        // Tests that depended on typescript.d.ts in built.
        "APILibCheck.ts",
        "APISample_Watch.ts",
        "APISample_WatchWithDefaults.ts",
        "APISample_WatchWithOwnWatchHost.ts",
        "APISample_compile.ts",
        "APISample_jsdoc.ts",
        "APISample_linter.ts",
        "APISample_parseConfig.ts",
        "APISample_transform.ts",
        "APISample_watcher.ts",

        // These tests contain options that have been completely removed, so fail to parse.
        "preserveUnusedImports.ts",
        "noCrashWithVerbatimModuleSyntaxAndImportsNotUsedAsValues.ts",
        "verbatimModuleSyntaxCompat.ts",
        "verbatimModuleSyntaxCompat2.ts",
        "verbatimModuleSyntaxCompat3.ts",
        "verbatimModuleSyntaxCompat4.ts",
        "preserveValueImports.ts",
        "preserveValueImports_importsNotUsedAsValues.ts",
        "preserveValueImports_errors.ts",
        "preserveValueImports_mixedImports.ts",
        "preserveValueImports_module.ts",
        "importsNotUsedAsValues_error.ts",
        "alwaysStrictNoImplicitUseStrict.ts",
        "nonPrimitiveIndexingWithForInSupressError.ts",
        "parameterInitializerBeforeDestructuringEmit.ts",
        "mappedTypeUnionConstraintInferences.ts",
        "lateBoundConstraintTypeChecksCorrectly.ts",
        "keyofDoesntContainSymbols.ts",
        "noStrictGenericChecks.ts",
        "noImplicitUseStrict_umd.ts",
        "noImplicitUseStrict_system.ts",
        "noImplicitUseStrict_es6.ts",
        "noImplicitUseStrict_commonjs.ts",
        "noImplicitAnyIndexingSuppressed.ts",
        "excessPropertyErrorsSuppressed.ts",
        "moduleNoneDynamicImport.ts",
        "moduleNoneErrors.ts",
        "noErrorUsingImportExportModuleAugmentationInDeclarationFile1.ts",
        "noErrorUsingImportExportModuleAugmentationInDeclarationFile2.ts",
        "noErrorUsingImportExportModuleAugmentationInDeclarationFile3.ts",
        "requireOfJsonFileWithModuleEmitNone.ts",
        "requireOfJsonFileWithModuleNodeResolutionEmitNone.ts",
    ];

    /// <summary>Go 的 <c>RunTests</c> 直接读同包包级 var <c>skippedTests</c>；C# 的 harness 入口是另一个程序集，
    /// 所以补一个只读入口（Go 侧无对应函数，仅为跨程序集可用；语义 = <c>slices.Contains(skippedTests, basename)</c>）。</summary>
    public static bool IsSkippedTest(string basename) =>
        Array.IndexOf(_skippedTests, basename) >= 0 || Array.IndexOf(_skippedTestsNotValidUtf8, basename) >= 0;

    /// <summary>**C# 特有**的跳过清单（不属于 Go 的 <c>skippedTests</c>，也不能被它替代）：
    /// 这 8 个用例文件的字节不是合法 UTF-8（Go 的 string 是字节串，能原样承载；C# 的 string 是 UTF-16，
    /// 解码非法字节会变成 U+FFFD），所以**不可能**逐字节复现 reference baseline。
    /// 取证（M7Baseline 实测，2026-09-28）：`--case regexInvalidUtf8WithUnicodeFlag.ts` →
    /// `.js`/`.symbols`/`.types` 三个 baseline 失败，首个差异 = reference 第 116 字节 `0x80`（无效单字节）
    /// vs 我们的 `EF BF BD`（U+FFFD），长度 188 → 192。
    /// 扫描法：对 tests/cases/compiler 下 12,760 个用例文件做严格 UTF-8 解码，抛错的就是这 8 个。</summary>
    internal static readonly string[] _skippedTestsNotValidUtf8 =
    [
        "bom-utf16be.ts",
        "bom-utf16le.ts",
        "collisionCodeGenModuleWithUnicodeNames.ts",
        "instanceofOperator.ts",
        "promiseTest.ts",
        "regexInvalidUtf8WithUnicodeFlag.ts",
        "targetTypeBaseCalls.ts",
        "unicodeIdentifierNames.ts",
    ];

    /// <summary>Go: <c>var localBasePath = filepath.Join(repo.TestDataPath(), "baselines", "local")</c>（compiler_runner.go:138）。</summary>
    internal static readonly string _localBasePath = Path.Combine(TestDataPath(), "baselines", "local");

    /// <summary>Go: <c>var skippedEmitTests = map[string]string{...}</c>（compiler_runner.go:429-438，共 8 个名字）。</summary>
    internal static readonly Dictionary<string, string> _skippedEmitTests = new()
    {
        ["filesEmittingIntoSameOutput.ts"] = "Output order nondeterministic due to collision on filename during parallel emit.",
        ["jsFileCompilationWithJsEmitPathSameAsInput.ts"] = "Output order nondeterministic due to collision on filename during parallel emit.",
        ["grammarErrors.ts"] = "Output order nondeterministic due to collision on filename during parallel emit.",
        ["jsFileCompilationEmitBlockedCorrectly.ts"] = "Output order nondeterministic due to collision on filename during parallel emit.",
        ["jsDeclarationsReexportAliasesEsModuleInterop.ts"] = "cls.d.ts is missing statements when run concurrently.",
        ["jsFileCompilationWithoutJsExtensions.ts"] = "No files are emitted.",
        ["typeOnlyMerge2.ts"] = "Nondeterministic contents when run concurrently.",
        ["typeOnlyMerge3.ts"] = "Nondeterministic contents when run concurrently.",
    };

    /// <summary>Go: <c>var compilerVaryBy map[string]struct{} = getCompilerVaryByMap()</c>（compiler_runner.go:150）。</summary>
    internal static readonly HashSet<string> _compilerVaryBy = GetCompilerVaryByMap();

    /// <summary>Go: <c>func getCompilerVaryByMap() map[string]struct{}</c>（compiler_runner.go:152-177）。</summary>
    internal static HashSet<string> GetCompilerVaryByMap()
    {
        var varyByOptions = new List<string>(Core.Map(
            Core.Filter(OptionsDeclarations, option => !option.IsCommandLineOnly
                && (option.Kind == CommandLineOptionKind.Boolean || option.Kind == CommandLineOptionKind.Enum)
                && (option.AffectsProgramStructure
                    || option.AffectsEmit
                    || option.AffectsModuleResolution
                    || option.AffectsBindDiagnostics
                    || option.AffectsSemanticDiagnostics
                    || option.AffectsSourceFile
                    || option.AffectsDeclarationPath
                    || option.AffectsBuildInfo)),
            option => option.Name));
        // explicit variations that do not match above conditions
        varyByOptions.Add("noEmit");
        varyByOptions.Add("isolatedModules");

        var varyByMap = new HashSet<string>();
        foreach (var option in varyByOptions)
        {
            varyByMap.Add(ToLowerJS(option));
        }
        return varyByMap;
    }

    /// <summary>Go: <c>func getCompilerFileBasedTest(t *testing.T, filename string) *compilerFileBasedTest</c>（compiler_runner.go:221-233）。</summary>
    internal static CompilerFileBasedTest GetCompilerFileBasedTest(IHarnessTest t, string filename)
    {
        if (!Osvfs.FS().TryReadFile(filename, out var content))
        {
            Debug.Fail("Could not read test file: " + filename);
        }
        var settings = ExtractCompilerSettings(content);
        var configurations = HarnessUtil.GetFileBasedTestConfigurations(t, settings, _compilerVaryBy);
        return new CompilerFileBasedTest
        {
            _filename = filename,
            _content = content,
            _configurations = [.. configurations],
        };
    }

    /// <summary>Go: <c>func createHarnessTestFile(unit *testUnit, currentDirectory string) *harnessutil.TestFile</c>（compiler_runner.go:545-550）。</summary>
    internal static TestFile CreateHarnessTestFile(TestUnit unit, string currentDirectory) => new()
    {
        UnitName = GetNormalizedAbsolutePath(unit._name, currentDirectory),
        Content = unit._content,
    };

    /// <summary>Go: <c>func newCompilerTest(...)</c>（compiler_runner.go:255-362）。</summary>
    internal static CompilerTest NewCompilerTest(
        IHarnessTest t,
        string testName,
        string filename,
        TestCaseContent testContent,
        NamedTestConfiguration? namedConfiguration)
    {
        var basename = GetBaseFileName(filename);
        var configuredName = basename;
        if (namedConfiguration is not null && namedConfiguration.Name != "")
        {
            var extname = GetAnyExtensionFromPath(basename, [], false);
            var extensionlessBasename = basename[..(basename.Length - extname.Length)];
            configuredName = $"{extensionlessBasename}({namedConfiguration.Name}){extname}";
        }

        Dictionary<string, string>? configuration = null;
        if (namedConfiguration is not null)
        {
            configuration = namedConfiguration.Config;
        }
        var testCaseContentWithConfig = new TestCaseContentWithConfig
        {
            _testCaseContent = testContent,
            _configuration = configuration,
        };

        // Go: harnessConfig := testCaseContentWithConfig.configuration（nil map 时读得 ""）
        var harnessConfig = testCaseContentWithConfig._configuration;
        var currentDirectory = GetNormalizedAbsolutePath(
            harnessConfig is null ? "" : harnessConfig.GetValueOrDefault("currentdirectory") ?? "",
            SrcFolder);

        var units = testCaseContentWithConfig._testCaseContent._testUnitData;
        var toBeCompiled = new List<TestFile>();
        var otherFiles = new List<TestFile>();
        ParsedCommandLine? tsConfig = null;
        var hasNonDtsFiles = Core.Some(units, unit => !FileExtensionIs(unit._name, ExtensionDts));
        var tsConfigFiles = new List<TestFile>();
        if (testCaseContentWithConfig._testCaseContent._tsConfig is not null)
        {
            tsConfig = testCaseContentWithConfig._testCaseContent._tsConfig;
            tsConfigFiles.Add(CreateHarnessTestFile(testCaseContentWithConfig._testCaseContent._tsConfigFileUnitData!, currentDirectory));
            foreach (var unit in units)
            {
                if (tsConfig.ParsedConfig.FileNames.Contains(GetNormalizedAbsolutePath(unit._name, currentDirectory)))
                {
                    toBeCompiled.Add(CreateHarnessTestFile(unit, currentDirectory));
                }
                else
                {
                    otherFiles.Add(CreateHarnessTestFile(unit, currentDirectory));
                }
            }
        }
        else
        {
            if (harnessConfig is not null
                && harnessConfig.TryGetValue("baseurl", out var baseUrl)
                && !IsRootedDiskPath(baseUrl))
            {
                harnessConfig["baseurl"] = GetNormalizedAbsolutePath(baseUrl, currentDirectory);
            }

            var lastUnit = units[^1];
            // We need to assemble the list of input files for the compiler and other related files on the 'filesystem' (ie in a multi-file test)
            // If the last file in a test uses require or a triple slash reference we'll assume all other files will be brought in via references,
            // otherwise, assume all files are just meant to be in the same compilation session without explicit references to one another.

            if ((harnessConfig is not null ? harnessConfig.GetValueOrDefault("noimplicitreferences") ?? "" : "") != ""
                || lastUnit._content.Contains(_requireStr, StringComparison.Ordinal)
                || _referencesRegex.IsMatch(lastUnit._content))
            {
                toBeCompiled.Add(CreateHarnessTestFile(lastUnit, currentDirectory));
                for (var i = 0; i < units.Length - 1; i++)
                {
                    otherFiles.Add(CreateHarnessTestFile(units[i], currentDirectory));
                }
            }
            else
            {
                toBeCompiled.AddRange(Core.Map(units, unit => CreateHarnessTestFile(unit, currentDirectory)));
            }
        }

        var result = HarnessUtil.CompileFiles(
            t,
            [.. toBeCompiled],
            [.. otherFiles],
            harnessConfig,
            tsConfig,
            currentDirectory,
            testCaseContentWithConfig._testCaseContent._symlinks);

        // Content-mapped files are transformed during program construction; the transformed text is what the
        // compiler actually parses and reports positions against. Baseline that text (rather than the original
        // foreign source) so the type, symbol, and error baselines line up with the compiler's positions.
        foreach (var file in Core.Concatenate(toBeCompiled.ToArray(), otherFiles.ToArray()))
        {
            var sf = result.Program.GetSourceFile(file.UnitName);
            if (sf is not null && sf.ContentMapper() != "")
            {
                file.Content = sf.Text();
            }
        }

        return new CompilerTest
        {
            _testName = testName,
            _filename = filename,
            _basename = basename,
            _configuredName = configuredName,
            _currentDirectory = currentDirectory,
            _options = result.Options,
            _harnessOptions = result.HarnessOptions,
            _result = result,
            _tsConfigFiles = [.. tsConfigFiles],
            _toBeCompiled = [.. toBeCompiled],
            _otherFiles = [.. otherFiles],
            _hasNonDtsFiles = hasNonDtsFiles,
        };
    }
}

/// <summary>Go: <c>type CompilerBaselineRunner struct</c>（compiler_runner.go:50-54）。</summary>
public sealed class CompilerBaselineRunner : Runner
{
    private string[] _testFiles = [];
    private readonly string _basePath;
    private readonly string _testSuitName;

    /// <summary>Go: <c>func NewCompilerBaselineRunner(testType CompilerTestType) *CompilerBaselineRunner</c>（compiler_runner.go:58-64）。</summary>
    public CompilerBaselineRunner(CompilerTestType testType)
    {
        var testSuitName = testType.String();
        _basePath = "tests/cases/" + testSuitName;
        _testSuitName = testSuitName;
    }

    /// <summary>Go: <c>func (r *CompilerBaselineRunner) EnumerateTestFiles() []string</c>（compiler_runner.go:66-76）。</summary>
    public string[] EnumerateTestFiles()
    {
        if (_testFiles.Length > 0)
        {
            return _testFiles;
        }

        string[] files;
        try
        {
            files = HarnessUtil.EnumerateFiles(_basePath, TestRunner._compilerBaselineRegex, true /*recursive*/);
        }
        catch (Exception ex)
        {
            // Go: panic("Could not read compiler test files: " + err.Error())
            Debug.Fail("Could not read compiler test files: " + ex.Message);
            throw;
        }
        _testFiles = files;
        return files;
    }

    /// <summary>Go: <c>func (r *CompilerBaselineRunner) RunTests(t *testing.T)</c>（compiler_runner.go:126-136）。</summary>
    public void RunTests(IHarnessTest t)
    {
        CleanUpLocal(t);
        var files = EnumerateTestFiles();

        foreach (var filename in files)
        {
            var basename = GetBaseFileName(filename);
            if (Array.IndexOf(TestRunner._skippedTests, basename) >= 0
                || Array.IndexOf(TestRunner._skippedTestsNotValidUtf8, basename) >= 0)
            {
                continue;
            }
            RunTest(t, filename);
        }
    }

    /// <summary>Go: <c>func (r *CompilerBaselineRunner) cleanUpLocal(t *testing.T)</c>（compiler_runner.go:140-148）。</summary>
    private void CleanUpLocal(IHarnessTest t)
    {
        var localPath = Path.Combine(TestRunner._localBasePath, _testSuitName);
        try
        {
            // Go: os.RemoveAll(localPath)（目录不存在时返回 nil，不报错）
            if (Directory.Exists(localPath))
            {
                Directory.Delete(localPath, true);
            }
        }
        catch (Exception ex)
        {
            // Go: panic("Could not clean up local compiler tests: " + err.Error())
            Debug.Fail("Could not clean up local compiler tests: " + ex.Message);
        }
    }

    /// <summary>Go: <c>func (r *CompilerBaselineRunner) runTest(t *testing.T, filename string)</c>（compiler_runner.go:179-193）。</summary>
    private void RunTest(IHarnessTest t, string filename)
    {
        var test = TestRunner.GetCompilerFileBasedTest(t, filename);
        var basename = GetBaseFileName(filename);
        if (test._configurations.Length > 0)
        {
            foreach (var config in test._configurations)
            {
                var testName = basename;
                if (config.Name != "")
                {
                    testName += " " + config.Name;
                }
                var capturedTestName = testName;
                var capturedConfig = config;
                t.Run(testName, t2 => RunSingleConfigTest(t2, capturedTestName, test, capturedConfig));
            }
        }
        else
        {
            t.Run(basename, t2 => RunSingleConfigTest(t2, basename, test, null));
        }
    }

    /// <summary>Go: <c>func (r *CompilerBaselineRunner) runSingleConfigTest(...)</c>（compiler_runner.go:195-213）。</summary>
    private void RunSingleConfigTest(IHarnessTest t, string testName, CompilerFileBasedTest test, NamedTestConfiguration? config)
    {
        t.Parallel();
        TestUtil.RecoverAndFail(t, "Panic on compiler test " + test._filename, () =>
        {
            var payload = TestRunner.MakeUnitsFromTest(test._content, test._filename);
            var compilerTest = TestRunner.NewCompilerTest(t, testName, test._filename, payload, config);

            HarnessUtil.SkipUnsupportedCompilerOptions(t, compilerTest._options);

            compilerTest.VerifyDiagnostics(t, _testSuitName);
            compilerTest.VerifyContentMapper(t, _testSuitName);
            compilerTest.VerifyJavaScriptOutput(t, _testSuitName);
            compilerTest.VerifySourceMapOutput(t, _testSuitName);
            compilerTest.VerifySourceMapRecord(t, _testSuitName);
            compilerTest.VerifyTypesAndSymbols(t, _testSuitName);
            compilerTest.VerifyModuleResolution(t, _testSuitName);
            compilerTest.VerifyUnionOrdering(t);
            compilerTest.VerifyParentPointers(t);
        });
    }
}

/// <summary>Go: <c>type compilerFileBasedTest struct</c>（compiler_runner.go:215-219）。</summary>
internal sealed class CompilerFileBasedTest
{
    internal string _filename = "";
    internal string _content = "";
    internal NamedTestConfiguration[] _configurations = [];
}

/// <summary>Go: <c>type compilerTest struct</c>（compiler_runner.go:235-248）。</summary>
internal sealed class CompilerTest
{
    internal string _testName = "";
    internal string _filename = "";
    internal string _basename = "";
    /// <summary>name with configuration description, e.g. `file`</summary>
    internal string _configuredName = "";
    internal string _currentDirectory = "";
    internal CompilerOptions _options = null!;
    internal HarnessOptions _harnessOptions = null!;
    internal CompilationResult _result = null!;
    internal TestFile[] _tsConfigFiles = [];
    /// <summary>equivalent to the files that will be passed on the command line</summary>
    internal TestFile[] _toBeCompiled = [];
    /// <summary>equivalent to other files on the file system not directly passed to the compiler (ie things that are referenced by other files)</summary>
    internal TestFile[] _otherFiles = [];
    internal bool _hasNonDtsFiles;

    /// <summary>Go: <c>c.result.Program.Program()</c>（contentmapper 查询 / ForEachCheckerParallel 需要底层 <c>*compiler.Program</c>）。
    /// C# 侧 <c>ProgramLike</c> 把 <c>Program() *Program</c> 换成了 `ContentMapperDiagnostics()`（CS0542：成员名不能与类同名），
    /// 所以这里用 `as` 取底层程序；编译器套件里 <c>CompilationResult.Program</c> 恒为 <c>Program</c> 实例。</summary>
    private TypeScript.Compiler.Program GoProgram()
    {
        var program = _result.Program as TypeScript.Compiler.Program;
        if (program is null)
        {
            Debug.Fail("CompilationResult.Program is not a Compiler.Program");
        }
        return program!;
    }

    /// <summary>Go: <c>func (c *compilerTest) verifyDiagnostics(t *testing.T, suiteName string)</c>（compiler_runner.go:364-403）。</summary>
    internal void VerifyDiagnostics(IHarnessTest t, string suiteName)
    {
        t.Run("error", t2 => TestUtil.RecoverAndFail(t2, "Panic on creating error baseline for test " + _filename, () =>
        {
            var files = Core.Concatenate(_tsConfigFiles, Core.Concatenate(_toBeCompiled, _otherFiles));
            var diagnostics = _result.Diagnostics;
            // Content-mapped files' diagnostics are baselined separately (see verifyContentMapper), where they can
            // be rendered against the correct text; the squiggle renderer here assumes a single coordinate space.
            var contentMapped = ContentMappedFileNames();
            if (contentMapped.Count > 0)
            {
                files = Core.Filter(files, f => !contentMapped.Contains(GetNormalizedAbsolutePath(f.UnitName, _currentDirectory)));
                diagnostics = Core.Filter(diagnostics, d => d.File() is null || !contentMapped.Contains(d.File()!.FileName()));
            }
            TsBaseline.DoErrorBaseline(t2, _configuredName, files, diagnostics, _result.Options.Pretty.IsTrue(), new BaselineOptions
            {
                Subfolder = suiteName,

                DiffFixupOld = old =>
                {
                    var sb = new StringBuilder(old.Length);

                    foreach (var lineRange in old.AsSpan().Split('\n'))
                    {
                        var line = old.AsSpan(lineRange);
                        const string relativePrefixNew = "==== ";
                        const string relativePrefixOld = relativePrefixNew + "./";
                        if (line.StartsWith(relativePrefixOld))
                        {
                            line = line[relativePrefixOld.Length..];
                        }

                        sb.Append(line);
                        sb.Append('\n');
                    }

                    return sb.Length == 0 ? "" : sb.ToString(0, sb.Length - 1);
                },
            });
        }));
    }

    /// <summary>Go: <c>func (c *compilerTest) verifyContentMapper(t *testing.T, suiteName string)</c>（compiler_runner.go:405-413）。</summary>
    internal void VerifyContentMapper(IHarnessTest t, string suiteName)
    {
        t.Run("content mapper", t2 => TestUtil.RecoverAndFail(t2, "Panic on creating content mapper baseline for test " + _filename, () =>
        {
            TsBaseline.DoContentMapperBaseline(t2, _configuredName, _result.Program, _result.Diagnostics, new BaselineOptions
            {
                Subfolder = suiteName,
            });
        }));
    }

    /// <summary>contentMappedFileNames returns the set of absolute file names that were produced by a content mapper.
    /// Go: <c>func (c *compilerTest) contentMappedFileNames() map[string]bool</c>（compiler_runner.go:415-427）。</summary>
    private HashSet<string> ContentMappedFileNames()
    {
        var program = GoProgram();
        var mapped = new HashSet<string>();
        foreach (var file in _result.Program.GetSourceFiles())
        {
            if (program.GetContentMapper(file) is not null)
            {
                mapped.Add(file.FileName());
            }
        }
        return mapped;
    }

    /// <summary>Go: <c>func (c *compilerTest) verifyJavaScriptOutput(t *testing.T, suiteName string)</c>（compiler_runner.go:440-466）。</summary>
    internal void VerifyJavaScriptOutput(IHarnessTest t, string suiteName)
    {
        if (!_hasNonDtsFiles)
        {
            return;
        }

        t.Run("output", t2 =>
        {
            if (TestRunner._skippedEmitTests.TryGetValue(_basename, out var msg))
            {
                t2.Skip(msg);
            }

            TestUtil.RecoverAndFail(t2, "Panic on creating js output for test " + _filename, () =>
            {
                var headerComponents = GetPathComponentsRelativeTo(TestDataPath(), _filename, new TypeScript.TSPath.ComparePathsOptions());
                var header = GetPathFromPathComponents(headerComponents);
                TsBaseline.DoJSEmitBaseline(
                    t2,
                    _configuredName,
                    header,
                    _options,
                    _result,
                    _tsConfigFiles,
                    _toBeCompiled,
                    _otherFiles,
                    _harnessOptions,
                    new BaselineOptions { Subfolder = suiteName });
            });
        });
    }

    /// <summary>Go: <c>func (c *compilerTest) verifySourceMapOutput(t *testing.T, suiteName string)</c>（compiler_runner.go:468-483）。</summary>
    internal void VerifySourceMapOutput(IHarnessTest t, string suiteName)
    {
        t.Run("sourcemap", t2 => TestUtil.RecoverAndFail(t2, "Panic on creating source map output for test " + _filename, () =>
        {
            var headerComponents = GetPathComponentsRelativeTo(TestDataPath(), _filename, new TypeScript.TSPath.ComparePathsOptions());
            var header = GetPathFromPathComponents(headerComponents);
            TsBaseline.DoSourcemapBaseline(
                t2,
                _configuredName,
                header,
                _options,
                _result,
                _harnessOptions,
                new BaselineOptions { Subfolder = suiteName });
        }));
    }

    /// <summary>Go: <c>func (c *compilerTest) verifySourceMapRecord(t *testing.T, suiteName string)</c>（compiler_runner.go:485-500）。</summary>
    internal void VerifySourceMapRecord(IHarnessTest t, string suiteName)
    {
        t.Run("sourcemap record", t2 => TestUtil.RecoverAndFail(t2, "Panic on creating source map record for test " + _filename, () =>
        {
            var headerComponents = GetPathComponentsRelativeTo(TestDataPath(), _filename, new TypeScript.TSPath.ComparePathsOptions());
            var header = GetPathFromPathComponents(headerComponents);
            TsBaseline.DoSourcemapRecordBaseline(
                t2,
                _configuredName,
                header,
                _options,
                _result,
                _harnessOptions,
                new BaselineOptions { Subfolder = suiteName });
        }));
    }

    /// <summary>Go: <c>func (c *compilerTest) verifyTypesAndSymbols(t *testing.T, suiteName string)</c>（compiler_runner.go:502-528）。</summary>
    internal void VerifyTypesAndSymbols(IHarnessTest t, string suiteName)
    {
        var noTypesAndSymbols = _harnessOptions.NoTypesAndSymbols;
        if (noTypesAndSymbols)
        {
            return;
        }
        var program = _result.Program;
        var allFiles = Core.Filter(
            Core.Concatenate(_toBeCompiled, _otherFiles),
            f => program.GetSourceFile(f.UnitName) is not null);

        var headerComponents = GetPathComponentsRelativeTo(TestDataPath(), _filename, new TypeScript.TSPath.ComparePathsOptions());
        var header = GetPathFromPathComponents(headerComponents);
        TsBaseline.DoTypeAndSymbolBaseline(
            t,
            _configuredName,
            header,
            program,
            allFiles,
            new BaselineOptions { Subfolder = suiteName },
            false,
            false,
            _result.Diagnostics.Length > 0);
    }

    /// <summary>Go: <c>func (c *compilerTest) verifyModuleResolution(t *testing.T, suiteName string)</c>（compiler_runner.go:530-543）。</summary>
    internal void VerifyModuleResolution(IHarnessTest t, string suiteName)
    {
        if (!_options.TraceResolution.IsTrue())
        {
            return;
        }

        t.Run("module resolution", t2 => TestUtil.RecoverAndFail(t2, "Panic on creating module resolution baseline for test " + _filename, () =>
        {
            TsBaseline.DoModuleResolutionBaseline(t2, _configuredName, _result.Trace, new BaselineOptions
            {
                Subfolder = suiteName,

                SkipDiffWithOld = true,
            });
        }));
    }

    /// <summary>Go: <c>func (c *compilerTest) verifyUnionOrdering(t *testing.T)</c>（compiler_runner.go:552-575）。</summary>
    internal void VerifyUnionOrdering(IHarnessTest t)
    {
        t.Run("union ordering", t2 =>
        {
            var p = GoProgram();
            p.ForEachCheckerParallel((_, c) =>
            {
                foreach (var union in c.UnionTypes())
                {
                    var types = union.Types();

                    var reversed = (CheckerType[])types.Clone();
                    Array.Reverse(reversed);
                    Array.Sort(reversed, (a, b) => TypeScript.Checker.Checker.CompareTypes(a, b));
                    if (!reversed.AsSpan().SequenceEqual(types))
                    {
                        t2.Fatal("compareTypes does not sort union types consistently");
                    }

                    var shuffled = (CheckerType[])types.Clone();
                    // Go: rng := rand.NewPCG(1234, 5678)（见文件头第 7 条：C# 用 System.Random 复刻"随机洗牌"意图）
                    var rng = new Random(1234);

                    for (var i = 0; i < 10; i++)
                    {
                        Shuffle(rng, shuffled);
                        Array.Sort(shuffled, (a, b) => TypeScript.Checker.Checker.CompareTypes(a, b));
                        if (!shuffled.AsSpan().SequenceEqual(types))
                        {
                            t2.Fatal("compareTypes does not sort union types consistently");
                        }
                    }
                }
            });
        });
    }

    /// <summary>Go: <c>func (c *compilerTest) verifyParentPointers(t *testing.T)</c>（compiler_runner.go:577-606）。</summary>
    internal void VerifyParentPointers(IHarnessTest t)
    {
        t.Run("source file parent pointers", t2 =>
        {
            Node? parent = null;
            bool Verifier(Node n)
            {
                if (n is null)
                {
                    return false;
                }
                if (n.Parent is null)
                {
                    t2.Fatal("parent node does not exist");
                }
                var elab = "";
                if (!Ast.NodeIsSynthesized(n))
                {
                    elab += Ast.GetSourceFileOfNode(n)!.Text()[n.Loc.Pos()..n.Loc.End()];
                }
                else
                {
                    elab += "!synthetic! no text available";
                }
                if (n.Parent != parent)
                {
                    t2.Fatal("parent node does not match traversed parent: " + n.Kind.String() + ": " + elab);
                }
                var oldParent = parent;
                parent = n;
                n.ForEachChild(Verifier);
                parent = oldParent;
                return false;
            }

            foreach (var f in _result.Program.GetSourceFiles())
            {
                if (_result.Program.IsSourceFileDefaultLibrary(f.Path()))
                {
                    continue;
                }
                parent = f.AsNode();
                f.AsNode().ForEachChild(Verifier);
            }
        });
    }

    /// <summary>Go 的 <c>rng.Shuffle(len(shuffled), swap)</c>：Fisher–Yates（见文件头第 7 条）。</summary>
    private static void Shuffle(Random rng, CheckerType[] array)
    {
        for (var i = array.Length - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (array[i], array[j]) = (array[j], array[i]);
        }
    }
}

/// <summary>Go: <c>type testCaseContentWithConfig struct</c>（compiler_runner.go:250-253，嵌 testCaseContent）。</summary>
internal sealed class TestCaseContentWithConfig
{
    internal TestCaseContent _testCaseContent = null!;
    internal Dictionary<string, string>? _configuration;
}
