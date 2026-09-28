// ported from: internal/fourslash/tests/jsdocTypedefTagServices_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsdocTypedefTagServicesCase
{
    public static void TestJsdocTypedefTagServices(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: a.js\n/**\n * Doc comment\n * [|@typedef /*def*/[|{| \"contextRangeIndex\": 0 |}Product|]\n * @property {string} title\n |]*/\n/**\n * @type {[|/*use*/Product|]}\n */\nconst product = null;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "use", "type Product = {\n    title: string;\n}", "Doc comment");
                f.VerifyBaselineFindAllReferences(t, "use", "def");
                f.VerifyBaselineRename(t, null!, U.ToAny(f.Ranges().Skip(1)));
                f.VerifyBaselineDocumentHighlights(t, null!, U.ToAny(f.Ranges().Skip(1)));
                f.VerifyBaselineGoToDefinition(t, true, "use");
            }
            finally
            {
                done();
            }
        });
    }

}
