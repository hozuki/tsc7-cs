// ported from: internal/fourslash/tests/findAllRefs_importType_named_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefs_importType_namedCase
{
    public static void TestFindAllRefs_importType_named(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /a.ts\n/*1*/export type /*2*/T = number;\n/*3*/export type /*4*/U = string;\n// @Filename: /b.ts\nconst x: import(\"./a\")./*5*/T = 0;\nconst x: import(\"./a\")./*6*/U = 0;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1", "2", "3", "4", "5", "6");
            }
            finally
            {
                done();
            }
        });
    }

}
