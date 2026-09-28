// ported from: internal/fourslash/tests/diagnosticsDefaultImportMergedWithJSDocTypeAlias1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class DiagnosticsDefaultImportMergedWithJSDocTypeAlias1Case
{
    public static void TestDiagnosticsDefaultImportMergedWithJSDocTypeAlias1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @checkJs: true\n// @Filename: /lib/types.d.ts\nexport interface RunnerOptions {\n  dryRun?: boolean;\n}\n\n// @Filename: /lib/runner.js\n\"use strict\";\n\n/**\n * @typedef {import('./types.d.ts').RunnerOptions} RunnerOptions\n */\n\nvar EventEmitter = require(\"node:events\").EventEmitter;\n\nclass Runner extends EventEmitter {\n  constructor() { super(); }\n}\n\nmodule.exports = Runner;\n\n// @Filename: /lib/stats-collector.mjs\n/** @typedef {import('./runner.js')} Runner */\n\nimport Runner from \"./runner.js\";\n\nconst createStatsCollector = (runner) => runner && Runner;\n\nexport { createStatsCollector };\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/lib/stats-collector.mjs");
                f.VerifyNumberOfErrorsInCurrentFile(t, 2);
            }
            finally
            {
                done();
            }
        });
    }

}
