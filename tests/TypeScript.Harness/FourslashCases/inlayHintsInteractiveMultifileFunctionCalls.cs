// ported from: internal/fourslash/tests/inlayHintsInteractiveMultifileFunctionCalls_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSU = TypeScript.LsUtil.LsUtil;
using LSUP = TypeScript.LsUtil;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class InlayHintsInteractiveMultifileFunctionCallsCase
{
    public static void TestInlayHintsInteractiveMultifileFunctionCalls(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Target: esnext\n// @module: node18\n// @Filename: aaa.mts\nimport { helperB } from \"./bbb.mjs\";\nhelperB(\"hello, world!\");\n// @Filename: bbb.mts\nimport { helperC } from \"./ccc.mjs\";\nexport function helperB(bParam: string) {\n    helperC(bParam);\n}\n// @Filename: ccc.mts\nexport function helperC(cParam: string) {}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "./aaa.mts");
                f.VerifyBaselineInlayHints(t, null!, new LSUP.UserPreferences { InlayHints = new LSUP.InlayHintsPreferences { IncludeInlayParameterNameHints = LSU.IncludeInlayParameterNameHintsAll } });
            }
            finally
            {
                done();
            }
        });
    }

}
