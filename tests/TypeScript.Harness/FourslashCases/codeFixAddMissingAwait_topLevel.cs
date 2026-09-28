// ported from: internal/fourslash/tests/codeFixAddMissingAwait_topLevel_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixAddMissingAwait_topLevelCase
{
    public static void TestCodeFixAddMissingAwait_topLevel(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("declare function getPromise(): Promise<string>;\nconst p = getPromise();\nwhile (true) {\n  p/*0*/.toLowerCase();\n  getPromise()/*1*/.toLowerCase();\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFixNotAvailable(t, "addMissingAwait");
                f.VerifyCodeFixNotAvailable(t, "addMissingAwaitToInitializer");
            }
            finally
            {
                done();
            }
        });
    }

}
