// ported from: internal/fourslash/tests/quickInfoContextualObjectMethodJSDoc_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoContextualObjectMethodJSDocCase
{
    public static void TestQuickInfoContextualObjectMethodJSDoc(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\ninterface I {\n    /**\n     * Description of func.\n     * @param arg Description of arg.\n     */\n    func(arg: number): void\n}\n\nclass Foo {\n    constructor(i: I) {}\n}\n\nnew Foo({ func/*1*/() {} })\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(method) I.func(arg: number): void", "Description of func.\n\n*@param* `arg` \u2014 Description of arg.");
            }
            finally
            {
                done();
            }
        });
    }

}
