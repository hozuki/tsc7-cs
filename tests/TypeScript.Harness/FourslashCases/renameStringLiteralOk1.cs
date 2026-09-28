// ported from: internal/fourslash/tests/renameStringLiteralOk1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameStringLiteralOk1Case
{
    public static void TestRenameStringLiteralOk1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("declare function f(): '[|foo|]' | 'bar'\nclass Foo {\n    f = f()\n}\nconst d: 'foo' = 'foo'\ndeclare const ff: Foo\nff.f = '[|foo|]'"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineRenameAtRangesWithText(t, null!, "foo");
            }
            finally
            {
                done();
            }
        });
    }

}
