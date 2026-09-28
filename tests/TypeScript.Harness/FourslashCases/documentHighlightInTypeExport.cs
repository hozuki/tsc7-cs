// ported from: internal/fourslash/tests/documentHighlightInTypeExport_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class DocumentHighlightInTypeExportCase
{
    public static void TestDocumentHighlightInTypeExport(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /1.ts\ntype [|A|] = 1;\nexport { [|A|] as [|B|] };\n// @Filename: /2.ts\ntype [|A|] = 1;\nlet [|A|]: [|A|] = 1;\nexport { [|A|] as [|B|] };\n// @Filename: /3.ts\ntype [|A|] = 1;\nlet [|A|]: [|A|] = 1;\nexport type { [|A|] as [|B|] };"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineDocumentHighlights(t, null!, U.ToAny(f.Ranges()));
            }
            finally
            {
                done();
            }
        });
    }

}
