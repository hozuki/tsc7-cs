// ported from: internal/fourslash/tests/findAllRefsForDefaultExport02_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsForDefaultExport02Case
{
    public static void TestFindAllRefsForDefaultExport02(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/*1*/export default function /*2*/DefaultExportedFunction() {\n    return /*3*/DefaultExportedFunction;\n}\n\nvar x: typeof /*4*/DefaultExportedFunction;\n\nvar y = /*5*/DefaultExportedFunction();\n\n/*6*/namespace /*7*/DefaultExportedFunction {\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1", "2", "3", "4", "5", "6", "7");
            }
            finally
            {
                done();
            }
        });
    }

}
