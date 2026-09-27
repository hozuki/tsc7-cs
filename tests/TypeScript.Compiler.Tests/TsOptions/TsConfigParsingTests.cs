// ported from: internal/tsoptions/tsconfigparsing_test.go
//
// 覆盖范围（Go 测试函数 → 本文件的 [Fact]/[Theory]，方法名 = Go 名字加 Test 前缀）：
//   TestParseConfigFileTextToJson                                     （表驱动，每个输入一条数据）
//   TestParseJsonConfigFileContent / TestParseJsonSourceFileConfigFileContent
//                                                                    ParseJsonConfigFileContent（表驱动，每个用例 × 两个 api 各一条）
//   TestParseTypeAcquisition                                          （表驱动）
//   TestParseJsonConfigFileContentAcceptsJsonRepresentations
//   TestParseJsonConfigFileContentPreservesRaw
//   TestParseJsonConfigFileContentHandlesNullArrayElements
//   TestParseJsonConfigFileContentDefaultsCompileOnSaveToFalse
//   TestParseJsonSourceFileConfigFileContentReportsInvalidExtendedConfig
//   TestParseJsonSourceFileConfigFileContentWithEmptyExtendedConfig
//   TestParseJsonSourceFileConfigFileContentDoesNotDuplicateUnquotedKeyDiagnostics
//   TestParseJsonSourceFileConfigFileContentReportsQuestionTokenDiagnostics
//   TestParseNullEnumCompilerOptions
//   TestExtendedConfigErrorsAppearOnCacheHit（Go 的两个子测试 → 两个 [Fact]）
//   TestExtendedConfigConfigDirPathsAreNotCached
// 跳过清单见本文件末尾注释。
//
// 注意：测试方法必须带 Test 前缀。Go 的测试是包级函数，xunit 的是类成员；本类里再调产品函数
// （ParseConfigFileTextToJson / ParseJsonConfigFileContent / ParseTypeAcquisition）时，成员查找优先于
// `using static` 导入，同名会把产品函数遮住（编译期就是 CS1501）。
//
// 与 Go 的差异（都是"Go 侧设施未移植"导致的等价替换，不是放松断言）：
// 1) 表驱动用例的期望值来自 Go 测试本身用 baseline.Run 记录的
//    tsc/testdata/baselines/reference/config/tsconfigParsing/*.js（FileNames / Errors / CompilerOptions /
//    TypeAcquisition）。C# 侧没有 baseline 设施，所以这些期望值被机械地抽成
//    TsConfigParsingTests.Data.cs（生成器：artifacts/TsOptionsTests/GenerateParseCases.mjs），
//    断言改成结构化比较：文件列表逐项、诊断逐条（code + 位置 + 消息文本）、选项逐字段。
// 2) Go 的 `diagnosticwriter.FormatDiagnosticsWithColorAndContext` 未移植（依赖未移植的 diagnosticwriter 包），
//    这里按它的**头部行**格式写了一行版 FormatDiagnostic（`file:line:col - error TSxxxx: message` +
//    `convertToRelativePath(fileName, {CurrentDirectory: basePath, UseCaseSensitiveFileNames: true})`，
//    与 baseline 的记录格式逐字对齐；不含颜色与源码片段）。baseline 里同一诊断的头部行就是期望值。
// 3) Go 的 `json.MarshalIndentWrite(options)` 依赖 core.CompilerOptions 的 JSON mapper（docs/json.md §4 尚未落地），
//    所以 CompilerOptions 的断言改成 ReadOption 逐字段读取（枚举按 C# 的成员名比较，baseline 里的数字
//    在生成器里已按 Core/CompilerOptions.cs 的枚举值换成成员名）。
// 4) 主机：Go 用 tsoptionstest.NewVFSParseConfigHost = vfstest.FromMap + CurrentDirectory。
//    C# 侧用产品里的 vfstest 移植 TypeScript.Vfs.VfsTest.VfsTest.FromMap。
//    （不用 tests/Module/MemoryFileSystem.cs 的 MapFileSystem：它的 GetAccessibleEntries 返回的是
//    "相对该目录的完整子路径"而不是直接子项名，违反 FS 契约，会让 glob 枚举多出/漏掉文件；
//    该文件自己也写明"需要完整内存 FS 的调用方请用 Vfs 切片的实现"。）
// 5) Go 的 `t.Run` 子测试 → xunit 的 Theory 数据点（每个用例 × api 一条），失败信息里带用例名。
using System;
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.TSPath;
using TypeScript.TsOptions;
using TypeScript.Vfs;
using Xunit;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.TSPath.TSPath;
using static TypeScript.TsOptions.TsOptions;
using Diagnostic = TypeScript.Ast.Diagnostic;
using TPath = TypeScript.TSPath.Path;
using VfsTestMap = TypeScript.Vfs.VfsTest.VfsTest;

namespace TypeScript.Tests;

public class TsConfigParsingTests
{
    // ------------------------------------------------------------------
    // Go: tsoptionstest.VfsParseConfigHost
    // ------------------------------------------------------------------
    private sealed class VfsParseConfigHost(FS vfs, string currentDirectory) : ParseConfigHost
    {
        public FS FS() => vfs;

        public string GetCurrentDirectory() => currentDirectory;
    }

    // Go: tsoptionstest.NewVFSParseConfigHost(files, currentDirectory, useCaseSensitiveFileNames=true)
    private static ParseConfigHost NewHost(ParseConfigInput input) =>
        new VfsParseConfigHost(VfsTestMap.FromMap(input.Files, true), input.BasePath);

    // Go: getParsedWithJsonApi
    private static ParsedCommandLine ParseWithJsonApi(ParseConfigInput input, ParseConfigHost host, string basePath)
    {
        var configFileName = GetNormalizedAbsolutePath(input.ConfigFileName, basePath);
        var path = ToPath(input.ConfigFileName, basePath, host.FS().UseCaseSensitiveFileNames());
        var (parsed, _) = ParseConfigFileTextToJson(configFileName, path, input.JsonText);
        return ParseJsonConfigFileContent(parsed, host, basePath, null, configFileName, null, null);
    }

    // Go: getParsedWithJsonSourceFileApi
    private static ParsedCommandLine ParseWithJsonSourceFileApi(ParseConfigInput input, ParseConfigHost host, string basePath)
    {
        var configFileName = GetNormalizedAbsolutePath(input.ConfigFileName, basePath);
        var path = ToPath(input.ConfigFileName, basePath, host.FS().UseCaseSensitiveFileNames());
        var parsed = TypeScript.Parser.Parser.ParseSourceFile(
            new SourceFileParseOptions { FileName = configFileName, Path = path },
            input.JsonText,
            ScriptKind.JSON);
        var tsConfigSourceFile = new TsConfigSourceFile(parsed);
        return ParseJsonSourceFileConfigFileContent(
            tsConfigSourceFile,
            host,
            host.GetCurrentDirectory(),
            null,
            null,
            configFileName,
            null,
            null);
    }

    // ------------------------------------------------------------------
    // 诊断文本：Go 的 diagnosticwriter.FormatDiagnosticWithColorAndContext 只有头部行被 baseline 记录，
    // 这里按它的规则还原（WriteLocation 的 file:line:col + "- " + category + " TS<code>: " + message）。
    // ------------------------------------------------------------------
    private static string FormatDiagnostic(Diagnostic diagnostic, string basePath)
    {
        var text = $"{diagnostic.Category().Name()} TS{diagnostic.Code()}: {diagnostic}";
        if (diagnostic.File() is null)
        {
            return text;
        }

        var file = diagnostic.File()!;
        var (line, character) = TypeScript.Scanner.Scanner.GetECMALineAndUTF16CharacterOfPosition(file, diagnostic.Pos());
        var relativeFileName = ConvertToRelativePath(file.FileName(), new ComparePathsOptions(true, basePath));
        return $"{relativeFileName}:{line + 1}:{character.Value + 1} - {text}";
    }

    private static string FormatDiagnostics(IEnumerable<Diagnostic> diagnostics, string basePath)
    {
        var lines = new List<string>();
        foreach (var diagnostic in diagnostics)
        {
            lines.Add(FormatDiagnostic(diagnostic, basePath));
        }

        return string.Join("\n", lines);
    }

    // ------------------------------------------------------------------
    // baseline 里的 CompilerOptions 是按字段值比较的；这里把 C# 的字段反向格式化成生成器写下的文本。
    // Go 的 json.MarshalIndentWrite(options) 需要 core.CompilerOptions 的 JSON mapper（尚未移植）。
    // ------------------------------------------------------------------
    private static string Tri(Tristate value) => value switch
    {
        Tristate.TSTrue => "true",
        Tristate.TSFalse => "false",
        _ => "unknown",
    };

    private static string Join(IEnumerable<string>? values) => values is null ? "<null>" : string.Join(",", values);

    private static string PathsText(OrderedMap<string, string[]>? paths)
    {
        if (paths is null)
        {
            return "<null>";
        }

        var parts = new List<string>();
        foreach (var (key, value) in paths.Entries())
        {
            parts.Add(key + "=" + (value is null ? "<null>" : string.Join(",", value)));
        }

        return string.Join(";", parts);
    }

    private static string ReadOption(CompilerOptions options, string name) => name switch
    {
        "allowJs" => Tri(options.AllowJs),
        "allowSyntheticDefaultImports" => Tri(options.AllowSyntheticDefaultImports),
        "baseUrl" => options.BaseUrl,
        "configFilePath" => options.ConfigFilePath,
        "declarationDir" => options.DeclarationDir,
        "emitDecoratorMetadata" => Tri(options.EmitDecoratorMetadata),
        "esModuleInterop" => Tri(options.ESModuleInterop),
        "experimentalDecorators" => Tri(options.ExperimentalDecorators),
        "incremental" => Tri(options.Incremental),
        "jsx" => options.Jsx.ToString(),
        "lib" => Join(options.Lib),
        "maxNodeModuleJsDepth" => options.MaxNodeModuleJsDepth?.ToString() ?? "<null>",
        "module" => options.Module.ToString(),
        "moduleDetection" => options.ModuleDetection.ToString(),
        "moduleResolution" => options.ModuleResolution.ToString(),
        "noEmit" => Tri(options.NoEmit),
        "noImplicitAny" => Tri(options.NoImplicitAny),
        "outDir" => options.OutDir,
        "outFile" => options.OutFile,
        "paths" => PathsText(options.Paths),
        "pathsBasePath" => options.PathsBasePath,
        "removeComments" => Tri(options.RemoveComments),
        "resolveJsonModule" => Tri(options.ResolveJsonModule),
        "rootDir" => options.RootDir,
        "skipLibCheck" => Tri(options.SkipLibCheck),
        "sourceMap" => Tri(options.SourceMap),
        "strict" => Tri(options.Strict),
        "strictPropertyInitialization" => Tri(options.StrictPropertyInitialization),
        "target" => options.Target.ToString(),
        "tsBuildInfoFile" => options.TsBuildInfoFile,
        "types" => Join(options.Types),
        _ => throw new InvalidOperationException("unhandled expected option: " + name),
    };

    private static void CheckEqual(List<string> failures, string label, string expected, string actual)
    {
        if (expected != actual)
        {
            failures.Add($"{label}\n    expected: {expected}\n    actual:   {actual}");
        }
    }

    // xunit 的 Assert.Equal 有 (T,T) 与 (IEnumerable<T>,IEnumerable<T>) 两个重载，字符串序列统一走这里，
    // 避免重载/相等比较器选择的歧义。
    private static void AssertSequence(string[] expected, IEnumerable<string> actual) =>
        Assert.Equal(string.Join(",", expected), string.Join(",", actual));

    // ------------------------------------------------------------------
    // Go: TestParseConfigFileTextToJson（表驱动）
    // ------------------------------------------------------------------
    public static IEnumerable<object[]> ConfigFileTextToJsonData()
    {
        foreach (var (title, inputs) in TsConfigParsingTestData.ConfigFileTextToJsonCases)
        {
            foreach (var input in inputs)
            {
                yield return [title, input];
            }
        }
    }

    [Theory]
    [MemberData(nameof(ConfigFileTextToJsonData))]
    public void TestParseConfigFileTextToJson(string title, string jsonText)
    {
        // Go 用 baseline.Run 把 Config/Errors 写成 js 基线；这里逐条核对 baseline 记录的结构。
        var (config, errors) = ParseConfigFileTextToJson("/apath/tsconfig.json", (TPath)"/apath", jsonText);

        Assert.Empty(errors);

        switch (title)
        {
            case "returns empty config for file with only whitespaces":
            case "returns empty config for file with comments only":
                // Config:: {} —— 这几个输入没有根表达式，Go 的 convertToJson 返回 struct{}{}（空 JSON 对象），
                // C# 落成 new object()（见 TsConfigParsing.JsonObject.cs 的 ConvertToJson）。
                AssertEmptyConfig(config);
                break;

            case "returns empty config when config is empty object":
                // Config:: {} —— 这里是空对象字面量，返回的是空 OrderedMap。
                Assert.Equal(0, Assert.IsType<OrderedMap<string, object?>>(config).Size());
                break;

            case "returns config object without comments":
                // Config:: { "exclude": [ "file.d.ts" ] }
                AssertSequence(["file.d.ts"], ExcludeOf(Assert.IsType<OrderedMap<string, object?>>(config)));
                break;

            case "keeps string content untouched":
                // Config:: { "exclude": [ "xx//file.d.ts" ] } / { "exclude": [ "xx/*file.d.ts*/" ] }
                AssertSequence(
                    [jsonText.Contains("xx//file.d.ts") ? "xx//file.d.ts" : "xx/*file.d.ts*/"],
                    ExcludeOf(Assert.IsType<OrderedMap<string, object?>>(config)));
                break;

            case "handles escaped characters in strings correctly":
                // Config:: { "exclude": [ "xx\"//files" ] } / { "exclude": [ "xx\\" ] }
                AssertSequence(
                    [jsonText.Contains("end of line comment") ? "xx\\" : "xx\"//files"],
                    ExcludeOf(Assert.IsType<OrderedMap<string, object?>>(config)));
                break;

            case "returns object when users correctly specify library":
                // Config:: { "compilerOptions": { "lib": ["es5"] } } / { "compilerOptions": { "lib": ["es5","es6"] } }
                var map = Assert.IsType<OrderedMap<string, object?>>(config);
                var (compilerOptions, ok) = map.Get("compilerOptions");
                Assert.True(ok);
                var compilerOptionsMap = Assert.IsType<OrderedMap<string, object?>>(compilerOptions);
                string[] expectedLib = jsonText.Contains("\"es6\"") ? new[] { "es5", "es6" } : new[] { "es5" };
                AssertSequence(expectedLib, StringsOf(compilerOptionsMap.GetOrZero("lib")));
                break;

            default:
                throw new InvalidOperationException("unhandled case: " + title);
        }
    }

    /// <summary>Go 的"空配置"（baseline 记为 <c>{}</c>）：非 null，且没有任何键。</summary>
    private static void AssertEmptyConfig(object? config)
    {
        Assert.NotNull(config);
        if (config is OrderedMap<string, object?> map)
        {
            Assert.Equal(0, map.Size());
        }
    }

    private static string[] ExcludeOf(OrderedMap<string, object?> map)
    {
        var (exclude, ok) = map.Get("exclude");
        Assert.True(ok, "expected 'exclude' in parsed config");
        return StringsOf(exclude);
    }

    private static string[] StringsOf(object? value)
    {
        var array = Assert.IsType<object?[]>(value);
        var result = new string[array.Length];
        for (var i = 0; i < array.Length; i++)
        {
            result[i] = Assert.IsType<string>(array[i]);
        }

        return result;
    }

    // ------------------------------------------------------------------
    // Go: TestParseJsonConfigFileContent / TestParseJsonSourceFileConfigFileContent（parseJsonConfigFileTests 表）
    // ------------------------------------------------------------------
    public static IEnumerable<object[]> JsonConfigCaseData()
    {
        foreach (var testCase in TsConfigParsingTestData.JsonConfigCases)
        {
            yield return [testCase];
        }
    }

    [Theory]
    [MemberData(nameof(JsonConfigCaseData))]
    public void TestParseJsonConfigFileContent(ParseConfigCase testCase)
    {
        // Go 每个用例是一个 t.Run 子测试；这里一个 Theory 数据点对应用例的一个 api，输入逐条核对后汇总报错。
        var failures = new List<string>();
        for (var i = 0; i < testCase.Inputs.Length; i++)
        {
            var input = testCase.Inputs[i];
            var basePath = input.BasePath == ""
                ? GetNormalizedAbsolutePath(GetDirectoryPath(input.ConfigFileName), "")
                : input.BasePath;
            var host = NewHost(input);
            var parsed = testCase.UseSourceFileApi
                ? ParseWithJsonSourceFileApi(input, host, basePath)
                : ParseWithJsonApi(input, host, basePath);

            CheckParsedConfig(failures, $"input[{i}]", parsed, input, basePath);
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    private static void CheckParsedConfig(
        List<string> failures,
        string label,
        ParsedCommandLine parsed,
        ParseConfigInput input,
        string basePath)
    {
        CheckEqual(failures, $"{label} FileNames", string.Join(",", input.ExpectedFileNames), string.Join(",", parsed.FileNames()));
        CheckEqual(
            failures,
            $"{label} Errors",
            string.Join("\n", input.ExpectedErrors),
            FormatDiagnostics(parsed.Errors, basePath));

        var options = parsed.CompilerOptions();
        if (options is not null)
        {
            foreach (var (name, expected) in input.ExpectedOptions)
            {
                CheckEqual(failures, $"{label} CompilerOptions.{name}", expected, ReadOption(options, name));
            }
        }

        if (input.ExpectedTypeAcquisition is { } expectedTypeAcquisition)
        {
            var typeAcquisition = parsed.TypeAcquisition();
            if (typeAcquisition is null)
            {
                failures.Add($"{label} TypeAcquisition: expected a value, got null");
            }
            else
            {
                CheckEqual(failures, $"{label} TypeAcquisition.enable", expectedTypeAcquisition.Enable, Tri(typeAcquisition.Enable));
                CheckEqual(failures, $"{label} TypeAcquisition.include", string.Join(",", expectedTypeAcquisition.Include), JoinOrEmpty(typeAcquisition.Include));
                CheckEqual(failures, $"{label} TypeAcquisition.exclude", string.Join(",", expectedTypeAcquisition.Exclude), JoinOrEmpty(typeAcquisition.Exclude));
            }
        }
    }

    // baseline 里 TypeAcquisition 的 include/exclude 缺失与空数组无法区分（Go 的 omitzero）。
    private static string JoinOrEmpty(string[]? values) => values is null ? "" : string.Join(",", values);

    // ------------------------------------------------------------------
    // Go: TestParseTypeAcquisition（每用例 × 两个 api）
    // ------------------------------------------------------------------
    public static IEnumerable<object[]> TypeAcquisitionCaseData()
    {
        foreach (var testCase in TsConfigParsingTestData.TypeAcquisitionCases)
        {
            yield return [testCase];
        }
    }

    [Theory]
    [MemberData(nameof(TypeAcquisitionCaseData))]
    public void TestParseTypeAcquisition(ParseConfigCase testCase)
    {
        var failures = new List<string>();
        for (var i = 0; i < testCase.Inputs.Length; i++)
        {
            var input = testCase.Inputs[i];
            var host = NewHost(input);
            var parsed = testCase.UseSourceFileApi
                ? ParseWithJsonSourceFileApi(input, host, input.BasePath)
                : ParseWithJsonApi(input, host, input.BasePath);

            CheckParsedConfig(failures, $"input[{i}]", parsed, input, input.BasePath);
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    // ------------------------------------------------------------------
    // Go: TestParseJsonConfigFileContentAcceptsJsonRepresentations
    // ------------------------------------------------------------------
    [Fact]
    public void TestParseJsonConfigFileContentAcceptsJsonRepresentations()
    {
        var host = new VfsParseConfigHost(
            VfsTestMap.FromMap(new Dictionary<string, string> { { "/project/index.ts", "export {};" } }, true),
            "/project");

        var (orderedMap, parseErrors) = ParseConfigFileTextToJson(
            "/project/tsconfig.json",
            (TPath)"/project/tsconfig.json",
            "{\"compilerOptions\":{\"strict\":true},\"files\":[\"index.ts\"]}");
        Assert.Empty(parseErrors);

        var orderedMapWithTypedSlices = new OrderedMap<string, object?>();
        orderedMapWithTypedSlices.Set("compilerOptions", new Dictionary<string, object?> { { "strict", true } });
        orderedMapWithTypedSlices.Set("files", new string[] { "index.ts" });

        var tests = new List<(string Name, object? Json)>
        {
            ("ordered map", orderedMap),
            ("ordered map with typed slices", orderedMapWithTypedSlices),
            ("plain map", new Dictionary<string, object?>
            {
                { "compilerOptions", new Dictionary<string, object?> { { "strict", true } } },
                { "files", new object?[] { "index.ts" } },
            }),
            ("typed slices", new Dictionary<string, object?>
            {
                { "compilerOptions", new Dictionary<string, object?> { { "strict", true } } },
                { "files", new string[] { "index.ts" } },
            }),
        };

        foreach (var (name, json) in tests)
        {
            var parsed = ParseJsonConfigFileContent(json, host, "/project", null, "/project/tsconfig.json", null, null);
            AssertSequence(["/project/index.ts"], parsed.FileNames());
            Assert.True(parsed.CompilerOptions()!.Strict.IsTrue(), $"{name}: expected strict to be true");
            Assert.Empty(parsed.Errors);
        }
    }

    // ------------------------------------------------------------------
    // Go: TestParseJsonConfigFileContentPreservesRaw
    // ------------------------------------------------------------------
    [Fact]
    public void TestParseJsonConfigFileContentPreservesRaw()
    {
        var host = new VfsParseConfigHost(
            VfsTestMap.FromMap(new Dictionary<string, string> { { "/project/index.ts", "export {};" } }, true),
            "/project");

        var parsed = ParseJsonConfigFileContent(
            new Dictionary<string, object?>
            {
                { "files", new object?[] { "index.ts" } },
                { "customSetting", new Dictionary<string, object?> { { "enabled", true } } },
                { "compileOnSave", true },
            },
            host,
            "/project",
            null,
            "/project/tsconfig.json",
            null,
            null);

        Assert.Empty(parsed.Errors);
        Assert.True(parsed.CompileOnSave is not null && parsed.CompileOnSave.Value);

        var raw = Assert.IsType<OrderedMap<string, object?>>(parsed.Raw);
        var keys = new List<string>();
        foreach (var key in raw.Keys())
        {
            keys.Add(key);
        }

        AssertSequence(["compileOnSave", "customSetting", "files"], keys);
        Assert.True(raw.Has("customSetting"));
    }

    // ------------------------------------------------------------------
    // Go: TestParseJsonConfigFileContentHandlesNullArrayElements
    // ------------------------------------------------------------------
    [Fact]
    public void TestParseJsonConfigFileContentHandlesNullArrayElements()
    {
        var host = new VfsParseConfigHost(
            VfsTestMap.FromMap(new Dictionary<string, string> { { "/project/index.ts", "export {};" } }, true),
            "/project");

        foreach (var property in new[] { "files", "include", "exclude" })
        {
            var parsed = ParseJsonConfigFileContent(
                new Dictionary<string, object?> { { property, new object?[] { null } } },
                host,
                "/project",
                null,
                "/project/tsconfig.json",
                null,
                null);

            Assert.True(parsed.Errors.Length > 0, $"{property}: expected a diagnostic");
            Assert.Equal(
                Compiler_option_0_requires_a_value_of_type_1.Code(),
                parsed.Errors[0].Code());
        }
    }

    // ------------------------------------------------------------------
    // Go: TestParseJsonConfigFileContentDefaultsCompileOnSaveToFalse
    // ------------------------------------------------------------------
    [Fact]
    public void TestParseJsonConfigFileContentDefaultsCompileOnSaveToFalse()
    {
        var host = new VfsParseConfigHost(
            VfsTestMap.FromMap(new Dictionary<string, string> { { "/project/index.ts", "export {};" } }, true),
            "/project");

        var parsed = ParseJsonConfigFileContent(
            new Dictionary<string, object?> { { "files", new object?[] { "index.ts" } } },
            host,
            "/project",
            null,
            "/project/tsconfig.json",
            null,
            null);

        Assert.True(parsed.CompileOnSave is not null);
        Assert.False(parsed.CompileOnSave!.Value);
    }

    // Go: tsoptionstest.GetParsedCommandLine
    private static ParsedCommandLine GetParsedCommandLine(
        string jsonText,
        Dictionary<string, string> files,
        string currentDirectory,
        bool useCaseSensitiveFileNames)
    {
        var host = new VfsParseConfigHost(VfsTestMap.FromMap(files, useCaseSensitiveFileNames), currentDirectory);
        var configFileName = CombinePaths(currentDirectory, "tsconfig.json");
        var tsconfigSourceFile = NewTsconfigSourceFileFromFilePath(
            configFileName,
            ToPath(configFileName, currentDirectory, useCaseSensitiveFileNames),
            jsonText);
        return ParseJsonSourceFileConfigFileContent(
            tsconfigSourceFile,
            host,
            currentDirectory,
            null,
            null,
            configFileName,
            null,
            null);
    }

    // ------------------------------------------------------------------
    // Go: TestParseJsonSourceFileConfigFileContentReportsInvalidExtendedConfig
    // ------------------------------------------------------------------
    [Fact]
    public void TestParseJsonSourceFileConfigFileContentReportsInvalidExtendedConfig()
    {
        var files = new Dictionary<string, string>
        {
            { "/project/tsconfig.json", "{\n  \"extends\": \"./bad.json\"\n}" },
            // The parser recovers from this as object-like JSON, producing expected-token errors for ':', ',', ',', and '}'.
            { "/project/bad.json", "{ this is not json" },
            { "/project/main.ts", "export const x = 1;" },
        };
        var host = new VfsParseConfigHost(VfsTestMap.FromMap(files, true), "/project");
        const string configFileName = "/project/tsconfig.json";
        var configFile = NewTsconfigSourceFileFromFilePath(
            configFileName,
            ToPath(configFileName, host.GetCurrentDirectory(), host.FS().UseCaseSensitiveFileNames()),
            files[configFileName]);

        var parsed = ParseJsonSourceFileConfigFileContent(
            configFile,
            host,
            host.GetCurrentDirectory(),
            null,
            null,
            configFileName,
            null,
            null);

        var parseErrors = new List<Diagnostic>();
        foreach (var diagnostic in parsed.Errors)
        {
            if (diagnostic.Code() == X_0_expected.Code())
            {
                parseErrors.Add(diagnostic);
            }
        }

        string[] expectedParseErrorMessages = [":", ",", ",", "}"];
        int[] expectedParseErrorPositions = [7, 10, 14, 18];

        Assert.Equal(expectedParseErrorMessages.Length, parseErrors.Count);
        for (var i = 0; i < parseErrors.Count; i++)
        {
            Assert.Equal(expectedParseErrorMessages[i], parseErrors[i].MessageArgs()![0]);
            Assert.Equal(expectedParseErrorPositions[i], parseErrors[i].Pos());
            Assert.Equal("/project/bad.json", parseErrors[i].File()!.FileName());
        }
    }

    // ------------------------------------------------------------------
    // Go: TestParseJsonSourceFileConfigFileContentWithEmptyExtendedConfig
    // Extending an empty config file used to panic on nil Statements (#4265).
    // ------------------------------------------------------------------
    [Fact]
    public void TestParseJsonSourceFileConfigFileContentWithEmptyExtendedConfig()
    {
        var files = new Dictionary<string, string>
        {
            { "/project/tsconfig.json", "{\n  \"extends\": \"./base.json\"\n}" },
            { "/project/base.json", "" },
            { "/project/main.ts", "export const x = 1;" },
        };
        var host = new VfsParseConfigHost(VfsTestMap.FromMap(files, true), "/project");
        const string configFileName = "/project/tsconfig.json";
        var configFile = NewTsconfigSourceFileFromFilePath(
            configFileName,
            ToPath(configFileName, host.GetCurrentDirectory(), host.FS().UseCaseSensitiveFileNames()),
            files[configFileName]);

        var parsed = ParseJsonSourceFileConfigFileContent(
            configFile,
            host,
            host.GetCurrentDirectory(),
            null,
            null,
            configFileName,
            null,
            null);

        Assert.NotNull(parsed);
        AssertSequence(["/project/main.ts"], parsed.FileNames());
    }

    // ------------------------------------------------------------------
    // Go: TestParseJsonSourceFileConfigFileContentDoesNotDuplicateUnquotedKeyDiagnostics
    // ------------------------------------------------------------------
    [Fact]
    public void TestParseJsonSourceFileConfigFileContentDoesNotDuplicateUnquotedKeyDiagnostics()
    {
        var parsed = GetParsedCommandLine(
            "{\n  compilerOptions: {\n    strict: true\n  }\n}",
            new Dictionary<string, string> { { "/main.ts", "export const x = 1;" } },
            "/",
            true);

        var diags = parsed.GetConfigFileParsingDiagnostics();
        Assert.Equal(2, diags.Length);

        var expectedLocations = new (int Line, int Character)[] { (1, 2), (2, 4) };
        for (var index = 0; index < diags.Length; index++)
        {
            Assert.Equal(String_literal_with_double_quotes_expected.Code(), diags[index].Code());
            var (line, character) = TypeScript.Scanner.Scanner.GetECMALineAndUTF16CharacterOfPosition(diags[index].File()!, diags[index].Pos());
            Assert.Equal(expectedLocations[index].Line, line);
            Assert.Equal(expectedLocations[index].Character, (int)character);
        }
    }

    // ------------------------------------------------------------------
    // Go: TestParseJsonSourceFileConfigFileContentReportsQuestionTokenDiagnostics
    // ------------------------------------------------------------------
    [Fact]
    public void TestParseJsonSourceFileConfigFileContentReportsQuestionTokenDiagnostics()
    {
        var parsed = GetParsedCommandLine(
            "{\n  compilerOptions?: {\n    strict?: true\n  }\n}",
            new Dictionary<string, string> { { "/main.ts", "export const x = 1;" } },
            "/",
            true);

        var questionTokenDiagnostics = new List<Diagnostic>();
        foreach (var diagnostic in parsed.GetConfigFileParsingDiagnostics())
        {
            if (diagnostic.Code() == The_0_modifier_can_only_be_used_in_TypeScript_files.Code())
            {
                questionTokenDiagnostics.Add(diagnostic);
            }
        }

        Assert.Equal(2, questionTokenDiagnostics.Count);
        var expectedLocations = new (int Line, int Character)[] { (1, 17), (2, 10) };
        for (var index = 0; index < questionTokenDiagnostics.Count; index++)
        {
            var (line, character) = TypeScript.Scanner.Scanner.GetECMALineAndUTF16CharacterOfPosition(
                questionTokenDiagnostics[index].File()!,
                questionTokenDiagnostics[index].Pos());
            Assert.Equal(expectedLocations[index].Line, line);
            Assert.Equal(expectedLocations[index].Character, (int)character);
        }
    }

    // ------------------------------------------------------------------
    // Go: TestParseNullEnumCompilerOptions
    // ------------------------------------------------------------------
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TestParseNullEnumCompilerOptions(bool useSourceFileApi)
    {
        var input = new ParseConfigInput
        {
            JsonText = "{\n\t\t\t\"compilerOptions\": {\n\t\t\t\t\"target\": null,\n\t\t\t\t\"module\": null\n\t\t\t}\n\t\t}",
            ConfigFileName = "tsconfig.json",
            BasePath = "/",
            Files = new Dictionary<string, string> { { "/app.ts", "" }, { "/tsconfig.json", "" } },
        };
        input.Files["/tsconfig.json"] = input.JsonText;

        var host = new VfsParseConfigHost(VfsTestMap.FromMap(input.Files, true), input.BasePath);
        var parsed = useSourceFileApi
            ? ParseWithJsonSourceFileApi(input, host, input.BasePath)
            : ParseWithJsonApi(input, host, input.BasePath);

        Assert.Empty(parsed.Errors);
    }

    // ------------------------------------------------------------------
    // Go: memoCache（ExtendedConfigCache 的最小记忆化实现，跨多次解析模拟缓存命中）
    // ------------------------------------------------------------------
    private sealed class MemoCache : ExtendedConfigCache
    {
        private Dictionary<TPath, ExtendedConfigCacheEntry>? _m;

        public ExtendedConfigCacheEntry? GetExtendedConfig(string fileName, TPath path, TPath[]? resolutionStack, ParseConfigHost host)
        {
            _m ??= new Dictionary<TPath, ExtendedConfigCacheEntry>();
            if (_m.TryGetValue(path, out var entry))
            {
                return entry;
            }

            entry = ParseExtendedConfig(fileName, path, resolutionStack, host, this);
            _m[path] = entry;
            return entry;
        }
    }

    // Go 的闭包 parseConfig（两个子测试里重复定义，这里抽成局部函数）
    private static ParsedCommandLine ParseConfigWithCache(ParseConfigHost host, string configFileName, ExtendedConfigCache cache)
    {
        var cfgPath = ToPath(configFileName, host.GetCurrentDirectory(), host.FS().UseCaseSensitiveFileNames());
        Assert.True(host.FS().TryReadFile(configFileName, out var jsonText), $"missing {configFileName} in test fs");
        var tsConfigSourceFile = new TsConfigSourceFile(TypeScript.Parser.Parser.ParseSourceFile(
            new SourceFileParseOptions { FileName = configFileName, Path = cfgPath },
            jsonText,
            ScriptKind.JSON));
        return ParseJsonSourceFileConfigFileContent(
            tsConfigSourceFile,
            host,
            host.GetCurrentDirectory(),
            null,
            null,
            configFileName,
            null,
            cache);
    }

    // ------------------------------------------------------------------
    // Go: TestExtendedConfigErrorsAppearOnCacheHit
    // ------------------------------------------------------------------
    [Fact]
    public void TestExtendedConfigErrorsAppearOnCacheHitSingleConfigParsedTwice()
    {
        var files = new Dictionary<string, string>
        {
            { "/tsconfig.json", "{\n  \"extends\": \"./base.json\"\n}" },
            // 'excludes' instead of 'exclude' triggers diagnostic
            { "/base.json", "{\n  \"excludes\": [\"**/*.ts\"]\n}" },
            { "/app.ts", "export {}" },
        };

        var host = new VfsParseConfigHost(VfsTestMap.FromMap(files, true), "/");

        var cache = new MemoCache();
        var first = ParseConfigWithCache(host, "/tsconfig.json", cache);
        Assert.True(first.Errors.Length > 0, "expected diagnostics on first parse, got 0");
        var second = ParseConfigWithCache(host, "/tsconfig.json", cache);
        Assert.True(second.Errors.Length > 0, "expected diagnostics on second parse (cache hit), got 0");
    }

    [Fact]
    public void TestExtendedConfigErrorsAppearOnCacheHitTwoConfigsShareSameBase()
    {
        var files = new Dictionary<string, string>
        {
            { "/base.json", "{\n  \"excludes\": [\"**/*.ts\"]\n}" },
            { "/projA/tsconfig.json", "{\n  \"extends\": \"../base.json\"\n}" },
            { "/projB/tsconfig.json", "{\n  \"extends\": \"../base.json\"\n}" },
            { "/projA/app.ts", "export {}" },
            { "/projB/app.ts", "export {}" },
        };

        var host = new VfsParseConfigHost(VfsTestMap.FromMap(files, true), "/");

        var cache = new MemoCache();
        var first = ParseConfigWithCache(host, "/projA/tsconfig.json", cache);
        Assert.True(first.Errors.Length > 0, "expected diagnostics for projA parse, got 0");
        var second = ParseConfigWithCache(host, "/projB/tsconfig.json", cache);
        Assert.True(second.Errors.Length > 0, "expected diagnostics for projB parse (cache hit on base), got 0");
    }

    // ------------------------------------------------------------------
    // Go: TestExtendedConfigConfigDirPathsAreNotCached
    // ------------------------------------------------------------------
    [Fact]
    public void TestExtendedConfigConfigDirPathsAreNotCached()
    {
        var files = new Dictionary<string, string>
        {
            { "/tsconfig.base.json", "{\n  \"compilerOptions\": {\n    \"paths\": {\n      \"@pkg/*\": [\"${configDir}/src/*\"]\n    }\n  }\n}" },
            { "/packages/a/tsconfig.json", "{\n  \"extends\": \"../../tsconfig.base.json\"\n}" },
            { "/packages/b/tsconfig.json", "{\n  \"extends\": \"../../tsconfig.base.json\"\n}" },
            { "/packages/a/index.ts", "export {}" },
            { "/packages/b/index.ts", "export {}" },
        };

        var host = new VfsParseConfigHost(VfsTestMap.FromMap(files, true), "/");
        var cache = new MemoCache();

        ParsedCommandLine ParseConfig(string configFileName)
        {
            var (parsed, errors) = GetParsedCommandLineOfConfigFile(configFileName, null, null, host, cache);
            Assert.True(errors.Length == 0, $"unexpected errors parsing {configFileName}: {FormatDiagnostics(errors, "/")}");
            return parsed!;
        }

        ParseConfig("/packages/a/tsconfig.json");
        var paths = ParseConfig("/packages/b/tsconfig.json").CompilerOptions()!.Paths!.GetOrZero("@pkg/*");
        Assert.NotNull(paths);
        AssertSequence(["/packages/b/src/*"], paths);
    }
}

// ---------------------------------------------------------------------------
// 跳过清单（Go 用例 → 未移植依赖）：
//
// TestParseSrcCompiler / BenchmarkParseSrcCompiler
//   依赖 internal/repo.TestDataPath + internal/vfs/osvfs（真实仓库 fixtures/compiler/tsconfig.json）。
//   osvfs/repo 未移植，且 C# 侧不该在单测里读 Go 仓库。
//
// TestContentMappers / TestContentMapperOptionDiagnosticLocation /
// TestContentMappersAreInheritedFromExtendedConfig / TestContentMappersRequireFlag /
// TestUnresolvedContentMapperDoesNotRegisterExtensions / TestContentMappersValidation /
// TestContentMapperExtensionValidationUsesHostCaseSensitivity
//   依赖 tsoptions/contentmappers.go（TsOptionsModel 分片）与 TypeScript.ContentMapper 包（正被另一分片重写）：
//   需要 Mapper 的 package.json 解析（exec/dynamicConfig）、RunExternalCode 选项语义、
//   GetContentMapperOptionDiagnosticLocation 等，尚未落地。
//
// TestParseConfigFileTextToJson 的 baseline.Run 部分
//   已改写成结构化断言（见 ParseConfigFileTextToJson）：Go 把 Config/Errors 写成 baseline 文件对比，
//   这里改为逐条核对 baseline 里记录的结构（空对象、注释剥离、字符串内容、lib 数组）。
//   依赖 diagnosticwriter.FormatDiagnosticsWithColorAndContext 做文本对比的部分不移植（见文件头差异 2、3）。
//
// parseJsonConfigFileTests / TestParseTypeAcquisition 的 baseline 对比
//   期望值取自 Go 仓库里 baseline.Run 生成的 .js 基线（不是自编），由生成器抽成
//   TsConfigParsingTests.Data.cs；断言本身是结构化的（FileNames 序列 / 诊断 code+位置+文本 / 选项字段值）。
// ---------------------------------------------------------------------------
