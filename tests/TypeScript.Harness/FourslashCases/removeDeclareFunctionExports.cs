// ported from: internal/fourslash/tests/removeDeclareFunctionExports_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RemoveDeclareFunctionExportsCase
{
    public static void TestRemoveDeclareFunctionExports(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("declare namespace M {\n    function RegExp2(pattern: string): RegExp2;\n    export function RegExp2(pattern: string, flags: string): RegExp2;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToBOF(t);
                f.DeleteAtCaret(t, 8);
            }
            finally
            {
                done();
            }
        });
    }

}
