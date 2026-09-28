// ported from: internal/fourslash/tests/codeFixAddVoidToPromiseJS5_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixAddVoidToPromiseJS5Case
{
    public static void TestCodeFixAddVoidToPromiseJS5(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @target: esnext\n// @lib: es2015\n// @strict: true\n// @allowJS: true\n// @checkJS: true\n// @filename: main.js\n/** @type {Promise<number>} */\nconst p2 = new Promise(resolve => resolve());"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFixNotAvailable(t, "Add 'void' to Promise resolved without a value");
            }
            finally
            {
                done();
            }
        });
    }

}
