// ported from: internal/fourslash/tests/findAllRefsOnImportAliases_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsOnImportAliasesCase
{
    public static void TestFindAllRefsOnImportAliases(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("//@Filename: a.ts\nexport class /*0*/Class {\n}\n//@Filename: b.ts\nimport { /*1*/Class } from \"./a\";\n\nvar c = new /*2*/Class();\n//@Filename: c.ts\nexport { /*3*/Class } from \"./a\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "0", "1", "2");
            }
            finally
            {
                done();
            }
        });
    }

}
