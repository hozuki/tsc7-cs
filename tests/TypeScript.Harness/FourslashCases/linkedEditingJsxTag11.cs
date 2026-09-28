// ported from: internal/fourslash/tests/linkedEditingJsxTag11_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class LinkedEditingJsxTag11Case
{
    public static void TestLinkedEditingJsxTag11(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /customElements.tsx\nconst jsx = <fbt:enum knownProp=\"accepted\"\n    unknownProp=\"rejected\">\n</fbt:enum>;\n\nconst customElement = <custom-element></custom-element>;\n\nconst standardElement = \n   <Link href=\"/hello\" passHref>\n       <Button component=\"a\">\n           Next\n       </Button>\n   </Link>;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineLinkedEditing(t);
            }
            finally
            {
                done();
            }
        });
    }

}
