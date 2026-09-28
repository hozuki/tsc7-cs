// ported from: internal/fourslash/tests/documentHighlightReferenceDirective_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class DocumentHighlightReferenceDirectiveCase
{
    public static void TestDocumentHighlightReferenceDirective(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /a.ts\n/// <reference path=\"[|./b.ts|]\" />\n\nconst x = 1;\n\n// @filename: b.ts\nexport type Foo = number;\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineDocumentHighlights(t, null!, f.Ranges()[0]);
            }
            finally
            {
                done();
            }
        });
    }

}
