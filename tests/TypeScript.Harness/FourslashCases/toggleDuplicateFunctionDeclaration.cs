// ported from: internal/fourslash/tests/toggleDuplicateFunctionDeclaration_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ToggleDuplicateFunctionDeclarationCase
{
    public static void TestToggleDuplicateFunctionDeclaration(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class D { }\nD();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToBOF(t);
                f.Insert(t, "declare function D();");
                f.GoToBOF(t);
                f.DeleteAtCaret(t, 21);
            }
            finally
            {
                done();
            }
        });
    }

}
