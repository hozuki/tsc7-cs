// ported from: internal/fourslash/tests/renameStringLiteralOk_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameStringLiteralOkCase
{
    public static void TestRenameStringLiteralOk(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Foo {\n    f: '[|foo|]' | 'bar'\n}\nconst d: 'foo' = 'foo'\ndeclare const f: Foo\nf.f = '[|foo|]'\nf.f = `[|foo|]`"u8);
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
