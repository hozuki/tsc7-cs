// ported from: internal/tsoptions/commandlineparser_test.go（只搬与本切片相关的用例）
//
// 与 Go 测试的偏离（必读）：
// 1) Go 的 `assertParseResult` 把结果与 `testdata/baselines/reference/tsoptions/commandLineParsing/<用例>.js`
//    逐字段比较（那份 baseline 是 CompilerOptions 的完整 JSON + 诊断格式化文本）。本仓库**还没有** CompilerOptions
//    的 JSON 反射映射（docs §3.6 禁反射），也没有 diagnosticwriter/baseline 测试基建（属于 M7 的 harness），
//    所以这里只断言**可观察的行为**：`Options` 原始表里的值（Go baseline 里 CompilerOptions:: 段的来源）、
//    `FileNames`、以及**诊断码**（Go baseline 里 Errors:: 段的来源，逐条对应）。
// 2) 需要 vfs/tsoptionstest 的用例（`TestParseCommandLineTypeRootsRelativePath`、
//    `TestCustomConditionsNullOverride`、build 模式用例）本次跳过，理由见报告：`tsoptionstest`（内存 host）/
//    `GetParsedCommandLineOfConfigFile` 相关基建尚未齐备；响应文件用例用 `VfsTestMap.FromMap` 顶替
//    `osvfs.FS()` + `tsoptionstest.NewVFSParseConfigHost`（= Go 的 `vfstest.FromMap` + currentDirectory，
//    见下面测试文件内联的 `VfsParseConfigHost`）。
// 3) `--lib ""`（"Parse empty string of --lib"）一条**未搬**：Go 里 `ParseListTypeOption("")` 返回空列表且无错误、
//    i 不前移，实际行为与用例注释（"This test is an error because the empty string is falsey"）不一致，
//    且只有 baseline 能判定，见报告。
using System.Collections.Generic;
using TypeScript.Core;
using TypeScript.TsOptions;
using TypeScript.Vfs;
using Xunit;
using static TypeScript.TsOptions.TsOptions;
using VfsTestMap = TypeScript.Vfs.VfsTest.VfsTest;

namespace TypeScript.Tests;

public class CommandLineParserTests
{
    // Go: 每条子场景都用 `osvfs.FS()`（真实 FS）。这里用 Vfs 切片移植的内存 FS（`vfstest.FromMap`），
    // 非响应文件用例不会触碰文件系统。
    private static TestCommandLineParser Parse(string[] args, CommandLineOption[]? decls = null) =>
        TsOptionsExportTest.ParseCommandLineTestWorker(decls, args, VfsTestMap.FromMap(new Dictionary<string, string>(), true), "/project");

    private static object? Option(TestCommandLineParser parsed, string name)
    {
        var (value, ok) = parsed.Options.Get(name);
        Assert.True(ok, $"option '{name}' 应当出现在解析结果里");
        return value;
    }

    private static int[] ErrorCodes(TestCommandLineParser parsed)
    {
        var codes = new int[parsed.Errors.Length];
        for (var i = 0; i < codes.Length; i++)
        {
            codes[i] = parsed.Errors[i].Code();
        }
        return codes;
    }

    [Fact]
    public void ParseSingleOptionOfLibraryFlag()
    {
        // "--lib es6 0.ts"
        var parsed = Parse(["--lib", "es6", "0.ts"]);
        Assert.Empty(parsed.Errors);
        Assert.Equal(["0.ts"], parsed.FileNames);
        Assert.Equal(["lib.es2015.d.ts"], (object?[])Option(parsed, "lib")!);
    }

    [Fact]
    public void ParseMultipleOptionsOfLibraryFlags()
    {
        // "--lib es5,es2015.symbol.wellknown 0.ts"
        var parsed = Parse(["--lib", "es5,es2015.symbol.wellknown", "0.ts"]);
        Assert.Empty(parsed.Errors);
        Assert.Equal(["0.ts"], parsed.FileNames);
        Assert.Equal(["lib.es5.d.ts", "lib.es2015.symbol.wellknown.d.ts"], (object?[])Option(parsed, "lib")!);
    }

    [Fact]
    public void ParseInvalidOptionOfLibraryFlags()
    {
        // "--lib es5,invalidOption 0.ts"
        var parsed = Parse(["--lib", "es5,invalidOption", "0.ts"]);
        Assert.Equal([6046], ErrorCodes(parsed)); // Argument_for_0_option_must_be_Colon_1
        Assert.Equal(["lib.es5.d.ts"], (object?[])Option(parsed, "lib")!);
        Assert.Equal(["0.ts"], parsed.FileNames);
    }

    [Theory]
    [InlineData("--jsx")]
    [InlineData("--module")]
    [InlineData("--newLine")]
    [InlineData("--target")]
    [InlineData("--moduleResolution")]
    public void ParseEmptyOptionsOfEnumOption(string option)
    {
        // "0.ts <option>"：enum 类型选项在命令行末尾缺参数 → 类型不匹配 + enum 取值列表两条诊断。
        var parsed = Parse(["0.ts", option]);
        Assert.Equal([6044, 6046], ErrorCodes(parsed));
        Assert.Equal(["0.ts"], parsed.FileNames);
    }

    [Fact]
    public void ParseEmptyLibOptionNeedsAValue()
    {
        // Go: `lib` 的 Kind 是 **list**（declscompiler.go:358 起；enum 变体在 Go 里被注释掉了），所以末尾缺参数时
        // 只有 6044（Compiler_option_0_expects_an_argument），并且把 lib 设成空列表 —— 没有 enum 才有的 6046。
        var parsed = Parse(["0.ts", "--lib"]);
        Assert.Equal([6044], ErrorCodes(parsed));
        Assert.Empty((string[])Option(parsed, "lib")!);
        Assert.Equal(["0.ts"], parsed.FileNames);
    }

    [Fact]
    public void ParseMultipleCompilerFlagsWithInputFilesAtTheEnd()
    {
        // "--lib es5,es2015.symbol.wellknown --target es5 0.ts"
        var parsed = Parse(["--lib", "es5,es2015.symbol.wellknown", "--target", "es5", "0.ts"]);
        Assert.Empty(parsed.Errors);
        Assert.Equal(["lib.es5.d.ts", "lib.es2015.symbol.wellknown.d.ts"], (object?[])Option(parsed, "lib")!);
        Assert.Equal(ScriptTarget.ES5, (ScriptTarget)Option(parsed, "target")!);
        Assert.Equal(["0.ts"], parsed.FileNames);
    }

    [Fact]
    public void ParseMultipleCompilerFlagsWithInputFilesInTheMiddle()
    {
        // "--module commonjs --target es5 0.ts --lib es5,es2015.symbol.wellknown"
        var parsed = Parse(["--module", "commonjs", "--target", "es5", "0.ts", "--lib", "es5,es2015.symbol.wellknown"]);
        Assert.Empty(parsed.Errors);
        Assert.Equal(ModuleKind.CommonJS, (ModuleKind)Option(parsed, "module")!);
        Assert.Equal(ScriptTarget.ES5, (ScriptTarget)Option(parsed, "target")!);
        Assert.Equal(["lib.es5.d.ts", "lib.es2015.symbol.wellknown.d.ts"], (object?[])Option(parsed, "lib")!);
        Assert.Equal(["0.ts"], parsed.FileNames);
    }

    [Fact]
    public void ParseLibOptionWithExtraComma()
    {
        // "--lib es5, es7 0.ts"：列表项按逗号切分，空项被丢弃（`es7` 成为输入文件名）。
        var parsed = Parse(["--lib", "es5,", "es7", "0.ts"]);
        Assert.Empty(parsed.Errors);
        Assert.Equal(["lib.es5.d.ts"], (object?[])Option(parsed, "lib")!);
        Assert.Equal(["es7", "0.ts"], parsed.FileNames);
    }

    [Fact]
    public void ParseExplicitBooleanFlagValue()
    {
        var parsed = Parse(["--strictNullChecks", "false", "0.ts"]);
        Assert.Empty(parsed.Errors);
        // Go 的 raw options map 对 boolean 选项存的是 bool（`p.options.Set(opt.Name, false)`，见 Go 的
        // commandlineparser.go 的 "boolean" 分支），Tristate 是后面 CompilerOptions 解析阶段才转换的。
        Assert.Equal(false, Option(parsed, "strictNullChecks"));
        Assert.Equal(["0.ts"], parsed.FileNames);
    }

    [Fact]
    public void ParseNonBooleanArgumentAfterBooleanFlag()
    {
        // 布尔旗标的可选值只认 "true"/"false"，其它值不消耗参数（于是它成为输入文件名）。
        var parsed = Parse(["--noImplicitAny", "t", "0.ts"]);
        Assert.Empty(parsed.Errors);
        Assert.Equal(true, Option(parsed, "noImplicitAny"));
        Assert.Equal(["t", "0.ts"], parsed.FileNames);
    }

    [Fact]
    public void ParseImplicitBooleanFlagValue()
    {
        var parsed = Parse(["--strictNullChecks"]);
        Assert.Empty(parsed.Errors);
        Assert.Equal(true, Option(parsed, "strictNullChecks"));
        Assert.Empty(parsed.FileNames);
    }

    [Fact]
    public void ParseIncrementalAndTsBuildInfoFile()
    {
        var parsed = Parse(["--incremental", "--tsBuildInfoFile", "build.tsbuildinfo", "0.ts"]);
        Assert.Empty(parsed.Errors);
        Assert.Equal(true, Option(parsed, "incremental"));
        Assert.Equal("build.tsbuildinfo", Option(parsed, "tsBuildInfoFile"));
        Assert.Equal(["0.ts"], parsed.FileNames);
    }

    [Fact]
    public void AllowsTsconfigOnlyOptionToBeSetToNull()
    {
        // "--composite null -tsBuildInfoFile null 0.ts"（`-tsBuildInfoFile` 单横线也算选项名）
        var parsed = Parse(["--composite", "null", "-tsBuildInfoFile", "null", "0.ts"]);
        Assert.Empty(parsed.Errors);
        Assert.Null(Option(parsed, "composite"));
        Assert.Null(Option(parsed, "tsBuildInfoFile"));
        Assert.Equal(["0.ts"], parsed.FileNames);
    }

    [Fact]
    public void ErrorsOnInvalidExcludeDirectories()
    {
        var parsed = Parse(["--excludeDirectories", "**/../*", "0.ts"]);
        Assert.NotEmpty(parsed.Errors);
    }

    [Fact]
    public void ParseExcludeDirectories()
    {
        var parsed = Parse(["--excludeDirectories", "**/temp", "0.ts"]);
        Assert.Empty(parsed.Errors);
        Assert.Equal(["**/temp"], (object?[])Option(parsed, "excludeDirectories")!);
        Assert.Equal(["0.ts"], parsed.FileNames);
    }

    // ****** Watch Options ******

    [Theory]
    [InlineData("--watchFile", "UseFsEvents")]
    [InlineData("--watchDirectory", "FixedPollingInterval")]
    [InlineData("--fallbackPolling", "PriorityInterval")]
    public void ParseWatchEnumOption(string option, string value)
    {
        // 编译器模式不认识 watch 选项 → 走 WatchNameMap（用 watch 的诊断集）。
        var parsed = Parse([option, value, "0.ts"]);
        Assert.Empty(parsed.Errors);
        var name = option[2..];
        Assert.NotNull(Option(parsed, name));
        Assert.Equal(["0.ts"], parsed.FileNames);
    }

    [Fact]
    public void ParseSynchronousWatchDirectory()
    {
        var parsed = Parse(["--synchronousWatchDirectory", "0.ts"]);
        Assert.Empty(parsed.Errors);
        Assert.Equal(true, Option(parsed, "synchronousWatchDirectory"));
        Assert.Equal(["0.ts"], parsed.FileNames);
    }

    [Fact]
    public void ErrorsOnMissingArgumentToFallbackPolling()
    {
        var parsed = Parse(["0.ts", "--fallbackPolling"]);
        Assert.Equal([5080, 6046], ErrorCodes(parsed)); // Watch_option_0_requires_a_value_of_type_1 + enum 取值列表
        Assert.Equal(["0.ts"], parsed.FileNames);
    }

    [Fact]
    public void HandlesDidYouMeanForMisspeltFlags()
    {
        var parsed = Parse(["--declarations", "--allowTS"]);
        Assert.Equal([5025, 5025], ErrorCodes(parsed)); // Unknown_compiler_option_0_Did_you_mean_1
        Assert.Empty(parsed.FileNames);
    }

    [Fact]
    public void HandlesMayOnlyBeUsedWithBuildFlags()
    {
        // "--build --clean --dry --force --verbose"：编译器模式下 build 选项族只有部分可用，
        // clean/dry/force/verbose 走 alternateMode（"may only be used with --build"），`--build` 自身也报错。
        var parsed = Parse(["--build", "--clean", "--dry", "--force", "--verbose"]);
        var codes = ErrorCodes(parsed);
        Assert.Equal(5, codes.Length);
        var mayOnlyBeUsedWithBuild = 0;
        foreach (var code in codes)
        {
            if (code == 5093)
            {
                mayOnlyBeUsedWithBuild++;
            }
        }
        Assert.Equal(4, mayOnlyBeUsedWithBuild);
    }

    // ****** tsconfig-only 选项的 null 语义（commandlineparser_test.go 的 TestParseCommandLineVerifyNull）******

    // Go: `slices.Concat(tsoptions.OptionsDeclarations, []*CommandLineOption{{Name: name, Kind: kind, IsTSConfigOnly: true}})`
    // —— "followed by another option" 用例里 `--strictNullChecks` 必须是**已知**选项，否则会多出一条
    // "Unknown compiler option" 诊断（Go 的 createVerifyNullForNonNullIncluded 也是带完整声明表的）。
    private static CommandLineOption[] TsConfigOnlyDecls(CommandLineOptionKind kind, string name) =>
    [
        .. OptionsDeclarations,
        new CommandLineOption
        {
            Name = name,
            Kind = kind,
            IsTSConfigOnly = true,
        },
    ];

    [Theory]
    [InlineData(CommandLineOptionKind.Boolean)]
    [InlineData(CommandLineOptionKind.Object)]
    [InlineData(CommandLineOptionKind.List)]
    [InlineData(CommandLineOptionKind.String)]
    [InlineData(CommandLineOptionKind.Number)]
    public void TsConfigOnlyOptionAllowsSettingItToNull(CommandLineOptionKind kind)
    {
        var decls = TsConfigOnlyDecls(kind, "optionName");
        var parsed = Parse(["--optionName", "null", "0.ts"], decls);
        Assert.Empty(parsed.Errors);
        Assert.Null(Option(parsed, "optionName"));
        Assert.Equal(["0.ts"], parsed.FileNames);
    }

    [Theory]
    [InlineData(CommandLineOptionKind.Object, "paths")]
    [InlineData(CommandLineOptionKind.List, "abc,xyz")]
    [InlineData(CommandLineOptionKind.String, "hello")]
    [InlineData(CommandLineOptionKind.Number, "10")]
    public void TsConfigOnlyNonNullValueIsAnError(CommandLineOptionKind kind, string value)
    {
        var decls = TsConfigOnlyDecls(kind, "optionName");
        var parsed = Parse(["--optionName", value, "0.ts"], decls);
        Assert.Equal([6064], ErrorCodes(parsed)); // ..._or_set_to_null_on_command_line
        Assert.Equal(["0.ts"], parsed.FileNames);
    }

    [Fact]
    public void TsConfigOnlyBooleanNonFalseValueIsAnError()
    {
        // Go: `--composite true`（boolean + tsconfig-only）只接受 false/null。
        var parsed = Parse(["--composite", "true", "0.ts"]);
        Assert.Equal([6230], ErrorCodes(parsed)); // ..._or_set_to_false_or_null_on_command_line
        Assert.Equal(["0.ts"], parsed.FileNames);
    }

    [Theory]
    [InlineData(CommandLineOptionKind.Boolean)]
    [InlineData(CommandLineOptionKind.Object)]
    [InlineData(CommandLineOptionKind.List)]
    [InlineData(CommandLineOptionKind.String)]
    [InlineData(CommandLineOptionKind.Number)]
    public void TsConfigOnlyOptionFollowedByAnotherOptionIsAnError(CommandLineOptionKind kind)
    {
        var decls = TsConfigOnlyDecls(kind, "optionName");
        var parsed = Parse(["0.ts", "--strictNullChecks", "--optionName"], decls);
        Assert.Single(parsed.Errors);
        var expectedCode = kind == CommandLineOptionKind.Boolean ? 6230 : 6064;
        Assert.Equal([expectedCode], ErrorCodes(parsed));
    }

    [Theory]
    [InlineData(CommandLineOptionKind.Boolean)]
    [InlineData(CommandLineOptionKind.Object)]
    [InlineData(CommandLineOptionKind.List)]
    [InlineData(CommandLineOptionKind.String)]
    [InlineData(CommandLineOptionKind.Number)]
    public void TsConfigOnlyOptionAsLastArgumentIsAnError(CommandLineOptionKind kind)
    {
        var decls = TsConfigOnlyDecls(kind, "optionName");
        var parsed = Parse(["0.ts", "--optionName"], decls);
        Assert.Single(parsed.Errors);
        var expectedCode = kind == CommandLineOptionKind.Boolean ? 6230 : 6064;
        Assert.Equal([expectedCode], ErrorCodes(parsed));
    }

    // ****** 响应文件（@@ / response files）******

    private sealed class VfsParseConfigHost(FS fs, string currentDirectory) : ParseConfigHost
    {
        public FS FS() => fs;

        public string GetCurrentDirectory() => currentDirectory;
    }

    [Fact]
    public void ResponseFileFinalTokenWithoutTrailingWhitespace()
    {
        var fs = VfsTestMap.FromMap(new Dictionary<string, string>
        {
            ["/project/args.txt"] = "--strict --outDir dist",
        }, true);
        var parsed = ParseCommandLine(["@args.txt"], new VfsParseConfigHost(fs, "/project"));
        Assert.Empty(parsed.Errors);
        Assert.True(parsed.CompilerOptions()!.Strict.IsTrue());
        Assert.Equal("/project/dist", parsed.CompilerOptions()!.OutDir);
    }

    [Fact]
    public void ResponseFileCyclic()
    {
        var fs = VfsTestMap.FromMap(new Dictionary<string, string>
        {
            ["/project/a.txt"] = "@/project/b.txt --strict",
            ["/project/b.txt"] = "@/project/a.txt --outDir dist",
        }, true);
        var parsed = ParseCommandLine(["@a.txt"], new VfsParseConfigHost(fs, "/project"));
        Assert.Empty(parsed.Errors);
        Assert.True(parsed.CompilerOptions()!.Strict.IsTrue());
        Assert.Equal("/project/dist", parsed.CompilerOptions()!.OutDir);
    }

    [Fact]
    public void ResponseFileQuotedArgument()
    {
        // Go: 只有以 '"' 开头的 token 才按引号解析；未闭合的引号产生 Unterminated_quoted_string_in_response_file_0。
        var fs = VfsTestMap.FromMap(new Dictionary<string, string>
        {
            ["/project/args.txt"] = "--outDir \"my dist\" --strict",
        }, true);
        var parsed = ParseCommandLine(["@args.txt"], new VfsParseConfigHost(fs, "/project"));
        Assert.Empty(parsed.Errors);
        Assert.Equal("/project/my dist", parsed.CompilerOptions()!.OutDir);
        Assert.True(parsed.CompilerOptions()!.Strict.IsTrue());
    }

    [Fact]
    public void ResponseFileUnterminatedQuoteIsAnError()
    {
        var fs = VfsTestMap.FromMap(new Dictionary<string, string>
        {
            ["/project/args.txt"] = "--outDir \"unterminated",
        }, true);
        var parsed = ParseCommandLine(["@args.txt"], new VfsParseConfigHost(fs, "/project"));
        // Go 的 parseResponseFile：未闭合的引号产生 6045，且该 token 不进入 args；args 里只剩 `--outDir`，
        // 它在末尾缺值 → 6044（Compiler_option_0_expects_an_argument）。两条诊断的先后顺序也与 Go 一致。
        Assert.Equal([6045, 6044], parsed.Errors.Select(static e => e.Code()));
    }

    [Fact]
    public void ResponseFileDoesNotPanic()
    {
        // response file
        var empty = TsOptionsExportTest.ParseCommandLineTestWorker(null, ["@"], VfsTestMap.FromMap(new Dictionary<string, string>(), true), "/project");
        Assert.NotEmpty(empty.Errors);

        var relative = TsOptionsExportTest.ParseCommandLineTestWorker(null, ["@blah"], VfsTestMap.FromMap(new Dictionary<string, string>(), true), "/project");
        Assert.NotEmpty(relative.Errors);
    }
}
