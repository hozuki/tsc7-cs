// ported from: internal/execute/tsctests/tsc_test.go 的 TestTscMissingFiles（tsc_test.go:290-355）。
//
// 与 Go 的偏离：`stringtestutil.Dedent` 的**逐字节结果**已由脚本按 Go 的 Dedent 算法算出后内联（见
// artifacts/gen-tsccases.js 与 artifacts/_case_missing.txt），不改变内容；其余为机械映射（见 TscCommandLineCases.cs）。
namespace TypeScript.Harness.TscTests;

using global::System.Collections.Generic;

using static TypeScript.Harness.TscTests.TscCaseRegistry;

internal static class TscMissingFilesCases
{
    public static IEnumerable<TscCaseEntry> All() =>
        FromInputs("TestTscMissingFiles", "commandLine", TestCases());

    private static IEnumerable<TscInput> TestCases()
    {
        yield return new TscInput
        {
            SubScenario = "file in tsconfig does not exist",
            Files = new FileMap
            {
                { "/home/src/workspaces/project/tsconfig.json", "{\n                    \"files\": [\"./src/doesNotExist.ts\"]\n                    }" },
            },
            CommandLineArgs = ["-p", "./tsconfig.json"],
        };
        yield return new TscInput
        {
            SubScenario = "extensionless file in tsconfig does not exist",
            Files = new FileMap
            {
                { "/home/src/workspaces/project/tsconfig.json", "{\n                    \"files\": [\"./src/doesNotExist\"]\n                    }" },
            },
            CommandLineArgs = ["-p", "./tsconfig.json"],
        };
        yield return new TscInput
        {
            SubScenario = "extensionless file in tsconfig exists",
            Files = new FileMap
            {
                { "/home/src/workspaces/project/tsconfig.json", "{\n                    \"files\": [\"./src/script\"]\n                    }" },
                { "/home/src/workspaces/project/src/script", "const n: number = \"s\";" },
            },
            CommandLineArgs = ["-p", "./tsconfig.json"],
        };
        yield return new TscInput
        {
            SubScenario = "extensionless file on command line exists",
            Files = new FileMap
            {
                { "/home/src/workspaces/project/script", "const n: number = \"s\";" },
            },
            CommandLineArgs = ["script"],
        };
        yield return new TscInput
        {
            SubScenario = "extensionless file in extended tsconfig in different folder does not exist",
            Files = new FileMap
            {
                { "/home/src/workspaces/project/src/tsconfig.json", "{\n                    \"extends\": \"./../tsconfig.base.json\",\n                    }" },
                { "/home/src/workspaces/project/src/oops.ts", "export const abc = 10;" },
                { "/home/src/workspaces/project/tsconfig.base.json", "{\n                    \"files\": [\"./oops\"],\n                    }" },
            },
            CommandLineArgs = ["-p", "./src/tsconfig.json"],
        };
    }
}
