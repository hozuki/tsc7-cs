// ported from: internal/fourslash/tests/jsDocServices_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsDocServicesCase
{
    public static void TestJsDocServices(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface /*I*/I {}\n\n/**\n * @param /*use*/[|foo|] I pity the foo\n */\nfunction f([|[|/*def*/{| \"contextRangeIndex\": 1 |}foo|]: I|]) {\n    return /*use2*/[|foo|];\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "use");
                f.VerifyQuickInfoIs(t, "(parameter) foo: I", "I pity the foo");
                f.VerifyBaselineFindAllReferences(t, "use", "def", "use2");
                f.VerifyBaselineRename(t, null!, f.Ranges()[0], f.Ranges()[2], f.Ranges()[3]);
                f.VerifyBaselineDocumentHighlights(t, null!, f.Ranges()[0], f.Ranges()[2], f.Ranges()[3]);
                f.VerifyBaselineGoToTypeDefinition(t, "use");
                f.VerifyBaselineGoToDefinition(t, false, "use");
            }
            finally
            {
                done();
            }
        });
    }

}
