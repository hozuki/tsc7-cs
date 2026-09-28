// ported from: internal/fourslash/tests/documentHighlights_moduleImport_filesToSearchWithInvalidFile_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class DocumentHighlights_moduleImport_filesToSearchWithInvalidFileCase
{
    public static void TestDocumentHighlights_moduleImport_filesToSearchWithInvalidFile(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /node_modules/@types/foo/index.d.ts\nexport const x: number;\n// @Filename: /a.ts\nimport * as foo from \"foo\";\nfoo.[|x|];\n// @Filename: /b.ts\nimport { [|x|] } from \"foo\";\n// @Filename: /c.ts\nimport { x } from \"foo\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineDocumentHighlightsWithOptions(t, null!, ["/a.ts", "/b.ts", "/unknown.ts"], U.ToAny(f.Ranges()));
            }
            finally
            {
                done();
            }
        });
    }

}
