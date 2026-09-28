// ported from: internal/execute/tsctests/tsc_test.go 的 TestTscCommandline（tsc_test.go:15-289）。
//
// 与 Go 的偏离：
// 1) Go 的 `stringtestutil.Dedent` → `StringTestUtil.Dedent`（同一移植）。
// 2) Go 的 `map[string]string{"K": "V"}` → `new Dictionary<string, string> { ["K"] = "V" }`；
//    Go 的 `FileMap{…}` → `new FileMap { { path, content } }`（值可以是 string 或 `VfsTest.Symlink`）。
// 3) Go 的 `new(outputIsTTY)` → 可空 bool 字段 `OutputIsTTY`（nil = 未指定）。
// 4) 用例顺序与 Go 逐条一致（registry 名 = `TestTscCommandline/commandLine/<subScenario>`）。
namespace TypeScript.Harness.TscTests;

using global::System.Collections.Generic;
using TypeScript.TestUtil.StringTestUtil;

using static TypeScript.Harness.TscTests.TscCaseRegistry;

internal static class TscCommandLineCases
{
    /// <summary>
    /// Go: `stringtestutil.Dedent(` `…` `)` 的**逐字节结果**（用脚本按 Go 的 Dedent 算法从源码算出，见
    /// artifacts/_dedent-tsc_test.json）。Go 源码里这段内容缩进用的是 tab，Dedent 把前导 tab 换成 4 空格后
    /// 再剥掉公共缩进 16，得到下面这个 4 空格缩进的文本。
    /// </summary>
    private const string _strictNoEmitTsconfig = "{\n    \"compilerOptions\": {\n        \"strict\": true,\n        \"noEmit\": true\n    }\n}";

    /// <summary>Go: tsc_test.go:254 的 Dedent 结果（该块用的是空格缩进，公共缩进为 0，故保留原缩进）。</summary>
    private const string _referencesEmptyFilesTsconfig = "{\n                    \"files\": [],\n                    \"references\": [\n                        {\n                            \"path\": \"./packages/pkg1\"\n                        },\n                    ],\n                }";

    /// <summary>Go: tsc_test.go:262 的 Dedent 结果（同上）。</summary>
    private const string _pkg1CompositeNoEmitTsconfig = "{\n                    \"compilerOptions\": {\n                        \"composite\": true,\n                        \"noEmit\": true\n                    },\n                    \"files\": [\n                        \"./index.ts\",\n                    ],\n                }";

    public static IEnumerable<TscCaseEntry> All() =>
        FromInputs("TestTscCommandline", "commandLine", TestCases());

    private static IEnumerable<TscInput> TestCases()
    {
        TscInput ColorTest(string subScenario, Dictionary<string, string> env, bool outputIsTTY) => new()
        {
            SubScenario = subScenario,
            Files = new FileMap
            {
                { "/home/src/workspaces/project/index.ts", "const x: string = 1;" },
            },
            CommandLineArgs = ["index.ts", "--noEmit"],
            Env = env,
            OutputIsTTY = outputIsTTY,
        };

        yield return new TscInput
        {
            SubScenario = "global diagnostics produced during ordinary semantic checking",
            Files = new FileMap
            {
                { "/home/src/workspaces/project/index.ts", "export function* values() { yield 1; }" },
            },
            CommandLineArgs = ["index.ts", "--noEmit"],
        };
        yield return new TscInput
        {
            SubScenario = "show help with ExitStatus.DiagnosticsPresent_OutputsSkipped",
            Env = new Dictionary<string, string>
            {
                ["TS_TEST_TERMINAL_WIDTH"] = "120",
            },
            CommandLineArgs = null,
        };
        yield return new TscInput
        {
            SubScenario = "show help with ExitStatus.DiagnosticsPresent_OutputsSkipped when host cannot provide terminal width",
            CommandLineArgs = null,
        };
        yield return ColorTest("does not add color when NO_COLOR is set", new Dictionary<string, string> { ["NO_COLOR"] = "true" }, true);
        yield return ColorTest("adds color when NO_COLOR is empty", new Dictionary<string, string> { ["NO_COLOR"] = "" }, true);
        yield return ColorTest("adds color when FORCE_COLOR is empty and output is not a TTY", new Dictionary<string, string> { ["FORCE_COLOR"] = "" }, false);
        yield return ColorTest("does not add color when FORCE_COLOR is zero", new Dictionary<string, string> { ["FORCE_COLOR"] = "0" }, true);
        yield return ColorTest("adds color when FORCE_COLOR is one and output is not a TTY", new Dictionary<string, string> { ["FORCE_COLOR"] = "1" }, false);
        yield return ColorTest("adds color when FORCE_COLOR is two and output is not a TTY", new Dictionary<string, string> { ["FORCE_COLOR"] = "2" }, false);
        yield return ColorTest("adds color when FORCE_COLOR is three and output is not a TTY", new Dictionary<string, string> { ["FORCE_COLOR"] = "3" }, false);
        yield return ColorTest("does not add color when FORCE_COLOR is four", new Dictionary<string, string> { ["FORCE_COLOR"] = "4" }, true);
        yield return ColorTest("adds color when FORCE_COLOR is true and output is not a TTY", new Dictionary<string, string> { ["FORCE_COLOR"] = "true" }, false);
        yield return ColorTest("does not add color when FORCE_COLOR is false", new Dictionary<string, string> { ["FORCE_COLOR"] = "false" }, true);
        yield return ColorTest("does not add color when FORCE_COLOR is invalid", new Dictionary<string, string> { ["FORCE_COLOR"] = "invalid" }, true);
        yield return ColorTest("FORCE_COLOR overrides NO_COLOR", new Dictionary<string, string> { ["NO_COLOR"] = "true", ["FORCE_COLOR"] = "true" }, false);
        yield return ColorTest("does not add color when TERM is dumb", new Dictionary<string, string> { ["TERM"] = "dumb" }, true);
        yield return ColorTest("FORCE_COLOR overrides dumb TERM", new Dictionary<string, string> { ["TERM"] = "dumb", ["FORCE_COLOR"] = "true" }, false);
        yield return new TscInput
        {
            SubScenario = "when build not first argument",
            CommandLineArgs = ["--verbose", "--build"],
        };
        yield return new TscInput
        {
            SubScenario = "malformed tsconfig property without value",
            Files = new FileMap
            {
                { "/home/src/workspaces/project/tsconfig.json", "{\"\" }" },
                { "/home/src/workspaces/project/index.ts", "" },
            },
            CommandLineArgs = null,
        };
        yield return new TscInput
        {
            SubScenario = "Initialized TSConfig with files options",
            CommandLineArgs = ["--init", "file0.st", "file1.ts", "file2.ts"],
        };
        yield return new TscInput
        {
            SubScenario = "Initialized TSConfig with boolean value compiler options",
            CommandLineArgs = ["--init", "--noUnusedLocals"],
        };
        yield return new TscInput
        {
            SubScenario = "Initialized TSConfig with enum value compiler options",
            CommandLineArgs = ["--init", "--target", "es5", "--jsx", "react"],
        };
        yield return new TscInput
        {
            SubScenario = "Initialized TSConfig with list compiler options",
            CommandLineArgs = ["--init", "--types", "jquery,mocha"],
        };
        yield return new TscInput
        {
            SubScenario = "Initialized TSConfig with list compiler options with enum value",
            CommandLineArgs = ["--init", "--lib", "es5,es2015.core"],
        };
        yield return new TscInput
        {
            SubScenario = "Initialized TSConfig with incorrect compiler option",
            CommandLineArgs = ["--init", "--someNonExistOption"],
        };
        yield return new TscInput
        {
            SubScenario = "Initialized TSConfig with incorrect compiler option value",
            CommandLineArgs = ["--init", "--lib", "nonExistLib,es5,es2015.promise"],
        };
        yield return new TscInput
        {
            SubScenario = "Initialized TSConfig with advanced options",
            CommandLineArgs = ["--init", "--declaration", "--declarationDir", "lib", "--skipLibCheck", "--noErrorTruncation"],
        };
        yield return new TscInput
        {
            SubScenario = "Initialized TSConfig with --help",
            CommandLineArgs = ["--init", "--help"],
        };
        yield return new TscInput
        {
            SubScenario = "Initialized TSConfig with --watch",
            CommandLineArgs = ["--init", "--watch"],
        };
        yield return new TscInput
        {
            SubScenario = "Initialized TSConfig with tsconfig.json",
            CommandLineArgs = ["--init"],
            Files = new FileMap
            {
                { "/home/src/workspaces/project/first.ts", "export const a = 1" },
                {
                    "/home/src/workspaces/project/tsconfig.json",
                    StringTestUtil.Dedent(_strictNoEmitTsconfig)
                },
            },
        };
        yield return new TscInput
        {
            SubScenario = "help",
            CommandLineArgs = ["--help"],
        };
        yield return new TscInput
        {
            SubScenario = "help all",
            CommandLineArgs = ["--help", "--all"],
        };
        yield return new TscInput
        {
            SubScenario = "Parse --lib option with file name",
            Files = new FileMap { { "/home/src/workspaces/project/first.ts", "export const Key = Symbol()" } },
            CommandLineArgs = ["--lib", "es6 ", "first.ts"],
        };
        yield return new TscInput
        {
            SubScenario = "noEmit with type error",
            Files = new FileMap { { "/home/src/workspaces/project/index.ts", "x = 5;" } },
            CommandLineArgs = ["--noEmit", "index.ts"],
        };
        yield return new TscInput
        {
            SubScenario = "option diagnostics are suppressed when there are syntactic errors",
            Files = new FileMap { { "/home/src/workspaces/project/a.ts", "const x: = 1;" } },
            CommandLineArgs = ["--strictPropertyInitialization", "--strictNullChecks", "false", "a.ts"],
        };
        yield return new TscInput
        {
            SubScenario = "non-object config root",
            Files = new FileMap
            {
                { "/home/src/workspaces/project/tsconfig.json", "[]" },
            },
            CommandLineArgs = [],
        };
        yield return new TscInput
        {
            SubScenario = "Project is empty string",
            Files = new FileMap
            {
                { "/home/src/workspaces/project/first.ts", "export const a = 1" },
                {
                    "/home/src/workspaces/project/tsconfig.json",
                    StringTestUtil.Dedent(_strictNoEmitTsconfig)
                },
            },
            CommandLineArgs = [],
        };
        yield return new TscInput
        {
            SubScenario = "Parse -p",
            Files = new FileMap
            {
                { "/home/src/workspaces/project/first.ts", "export const a = 1" },
                {
                    "/home/src/workspaces/project/tsconfig.json",
                    StringTestUtil.Dedent(_strictNoEmitTsconfig)
                },
            },
            CommandLineArgs = ["-p", "."],
        };
        yield return new TscInput
        {
            SubScenario = "Parse -p with path to tsconfig file",
            Files = new FileMap
            {
                { "/home/src/workspaces/project/first.ts", "export const a = 1" },
                {
                    "/home/src/workspaces/project/tsconfig.json",
                    StringTestUtil.Dedent(_strictNoEmitTsconfig)
                },
            },
            CommandLineArgs = ["-p", "/home/src/workspaces/project/tsconfig.json"],
        };
        yield return new TscInput
        {
            SubScenario = "Parse -p with path to tsconfig folder",
            Files = new FileMap
            {
                { "/home/src/workspaces/project/first.ts", "export const a = 1" },
                {
                    "/home/src/workspaces/project/tsconfig.json",
                    StringTestUtil.Dedent(_strictNoEmitTsconfig)
                },
            },
            CommandLineArgs = ["-p", "/home/src/workspaces/project"],
        };
        yield return new TscInput
        {
            SubScenario = "Parse -p with empty tsconfig file",
            Files = new FileMap
            {
                { "/home/src/workspaces/project/first.ts", "export const a = 1" },
                { "/home/src/workspaces/project/tsconfig.json", "" },
            },
            CommandLineArgs = ["-p", "."],
        };
        yield return new TscInput
        {
            SubScenario = "compiler option at top level of tsconfig",
            Files = new FileMap
            {
                { "/home/src/workspaces/project/index.ts", "" },
                { "/home/src/workspaces/project/tsconfig.json", "{ \"strict\": true }" },
            },
            CommandLineArgs = ["--pretty", "false"],
        };
        yield return new TscInput
        {
            SubScenario = "Parse enum type options",
            CommandLineArgs = ["--moduleResolution", "nodenext ", "first.ts", "--module", "nodenext", "--target", "esnext", "--moduleDetection", "auto", "--jsx", "react", "--newLine", "crlf"],
        };
        yield return new TscInput
        {
            SubScenario = "Parse watch interval option",
            Files = new FileMap
            {
                { "/home/src/workspaces/project/first.ts", "export const a = 1" },
                {
                    "/home/src/workspaces/project/tsconfig.json",
                    StringTestUtil.Dedent(_strictNoEmitTsconfig)
                },
            },
            CommandLineArgs = ["-w", "--watchInterval", "1000"],
        };
        yield return new TscInput
        {
            SubScenario = "Parse watch interval option without tsconfig.json",
            CommandLineArgs = ["-w", "--watchInterval", "1000"],
        };
        yield return new TscInput
        {
            SubScenario = "Config with references and empty file and refers to config with noEmit",
            Files = new FileMap
            {
                {
                    "/home/src/workspaces/project/tsconfig.json",
                    StringTestUtil.Dedent(_referencesEmptyFilesTsconfig)
                },
                {
                    "/home/src/workspaces/project/packages/pkg1/tsconfig.json",
                    StringTestUtil.Dedent(_pkg1CompositeNoEmitTsconfig)
                },
                { "/home/src/workspaces/project/packages/pkg1/index.ts", "export const a = 1;" },
            },
            CommandLineArgs = ["-p", "."],
        };
        yield return new TscInput
        {
            SubScenario = "locale",
            CommandLineArgs = ["--locale", "cs", "--version"],
        };
        yield return new TscInput
        {
            SubScenario = "bad locale",
            CommandLineArgs = ["--locale", "whoops", "--version"],
        };
    }
}
