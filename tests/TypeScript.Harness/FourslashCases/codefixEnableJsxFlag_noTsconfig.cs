// ported from: internal/fourslash/tests/codefixEnableJsxFlag_noTsconfig_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodefixEnableJsxFlag_noTsconfigCase
{
    public static void TestCodefixEnableJsxFlag_noTsconfig(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /dir/a.tsx\nexport const Component = () => <></>"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/dir/a.tsx");
                f.VerifyCodeFixNotAvailable(t);
            }
            finally
            {
                done();
            }
        });
    }

}
