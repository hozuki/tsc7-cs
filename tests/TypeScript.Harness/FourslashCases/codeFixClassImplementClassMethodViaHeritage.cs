// ported from: internal/fourslash/tests/codeFixClassImplementClassMethodViaHeritage_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixClassImplementClassMethodViaHeritageCase
{
    public static void TestCodeFixClassImplementClassMethodViaHeritage(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class C1 {\n    f1() {}\n}\n\nclass C2 extends C1 {\n\n}\n\nclass C3 implements C2 {[| \n    |]f2(){}\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyRangeAfterCodeFix(t, "f1(): void{\n    throw new Error(\"Method not implemented.\");\n}\n", false, 0, 0);
            }
            finally
            {
                done();
            }
        });
    }

}
