// ported from: internal/fourslash/tests/findAllRefsForDefaultExport03_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsForDefaultExport03Case
{
    public static void TestFindAllRefsForDefaultExport03(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/*1*/function /*2*/f() {\n    return 100;\n}\n\n/*3*/export default /*4*/f;\n\nvar x: typeof /*5*/f;\n\nvar y = /*6*/f();\n\n/*7*/namespace /*8*/f {\n    var local = 100;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1", "2", "3", "4", "5", "6", "7", "8");
            }
            finally
            {
                done();
            }
        });
    }

}
