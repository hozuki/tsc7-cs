// ported from: internal/fourslash/tests/documentHighlights02_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class DocumentHighlights02Case
{
    public static void TestDocumentHighlights02(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n// @Filename: a.ts\nfunction [|foo|] () {\n\treturn 1;\n}\n[|foo|]();\n// @Filename: b.ts\n/// <reference path=\"a.ts\"/>\n[|foo|]();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.GoToFile(t, "a.ts");
                f.GoToFile(t, "b.ts");
                f.VerifyBaselineDocumentHighlightsWithOptions(t, null!, ["a.ts", "b.ts"], U.ToAny(f.Ranges()));
            }
            finally
            {
                done();
            }
        });
    }

}
