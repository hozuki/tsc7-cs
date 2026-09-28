// ported from: internal/fourslash/tests/findReferencesJSXTagName3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindReferencesJSXTagName3Case
{
    public static void TestFindReferencesJSXTagName3(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @jsx: preserve\n// @Filename: /a.tsx\nnamespace JSX {\n    export interface Element { }\n    export interface IntrinsicElements {\n        [|[|/*1*/div|]: any;|]\n    }\n}\n\n[|const [|/*6*/Comp|] = () =>\n    [|<[|/*2*/div|]>\n        Some content\n        [|<[|/*3*/div|]>More content</[|/*4*/div|]>|]\n    </[|/*5*/div|]>|];|]\n\nconst x = [|<[|/*7*/Comp|]>\n    Content\n</[|/*8*/Comp|]>|];"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1", "2", "3", "4", "5", "6", "7", "8");
                f.VerifyBaselineDocumentHighlights(t, null!, f.Ranges()[1], f.Ranges()[5], f.Ranges()[7], f.Ranges()[8], f.Ranges()[9]);
                f.VerifyBaselineDocumentHighlights(t, null!, f.Ranges()[3], f.Ranges()[11], f.Ranges()[12]);
            }
            finally
            {
                done();
            }
        });
    }

}
