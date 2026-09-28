// ported from: internal/fourslash/tests/highlightsForExportFromUnfoundModule_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class HighlightsForExportFromUnfoundModuleCase
{
    public static void TestHighlightsForExportFromUnfoundModule(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: a.js\nimport foo from 'unfound';\nexport {\n  foo,\n};\n// @Filename: b.js\nexport {\n   /**/foo\n} from './a';"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifyBaselineRename(t, null!, "");
            }
            finally
            {
                done();
            }
        });
    }

}
