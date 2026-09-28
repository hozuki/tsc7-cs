// ported from: internal/fourslash/tests/codeFixInferFromCallInAssignment_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixInferFromCallInAssignmentCase
{
    public static void TestCodeFixInferFromCallInAssignment(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @noImplicitAny: true\nfunction inferAny( [| app |] ) {\n    const result = app.use('hi')\n    return result\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyRangeAfterCodeFix(t, "app: { use: (arg0: string) => any }", false, 0, 0);
            }
            finally
            {
                done();
            }
        });
    }

}
