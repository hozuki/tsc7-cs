// ported from: internal/fourslash/tests/findAllRefsJsDocImportTag_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsJsDocImportTagCase
{
    public static void TestFindAllRefsJsDocImportTag(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJS: true\n// @checkJs: true\n// @Filename: /b.ts\nexport interface A { }\n// @Filename: /a.js\n/**\n * @import { A } from \"./b\";\n */\n\n/**\n * @param { [|A/**/|] } a\n */\nfunction f(a) {}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "");
            }
            finally
            {
                done();
            }
        });
    }

}
