// ported from: internal/fourslash/tests/goToSource13_nodenext_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToSource13_nodenextCase
{
    public static void TestGoToSource13_nodenext(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /home/src/workspaces/project/node_modules/left-pad/package.json\n{\n  \"name\": \"left-pad\",\n  \"version\": \"1.3.0\",\n  \"description\": \"String left pad\",\n  \"main\": \"index.js\",\n  \"types\": \"index.d.ts\"\n}\n// @Filename: /home/src/workspaces/project/node_modules/left-pad/index.d.ts\ndeclare function leftPad(str: string|number, len: number, ch?: string|number): string;\ndeclare namespace leftPad { }\nexport = leftPad;\n// @Filename: /home/src/workspaces/project/node_modules/left-pad/index.js\nmodule.exports = leftPad;\nfunction /*end*/leftPad(str, len, ch) {}\n// @Filename: /home/src/workspaces/project/tsconfig.json\n{\n  \"compilerOptions\": {\n      \"module\": \"node16\",\n      \"lib\": [\"es5\"],\n      \"strict\": true,\n      \"outDir\": \"./out\",\n\n  }\n}\n// @Filename: /home/src/workspaces/project/index.mts\nimport leftPad = require(\"left-pad\");\n/*start*/leftPad(\"\", 4);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.VerifyBaselineGoToSourceDefinition(t, "start");
            }
            finally
            {
                done();
            }
        });
    }

}
