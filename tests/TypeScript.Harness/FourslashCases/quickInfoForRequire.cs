// ported from: internal/fourslash/tests/quickInfoForRequire_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoForRequireCase
{
    public static void TestQuickInfoForRequire(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("//@Filename: AA/BB.ts\nexport class a{}\n//@Filename: quickInfoForRequire_input.ts\nimport a = require(\"./AA/B/*1*/B\");\nimport b = require(`./AA/B/*2*/B`);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.VerifyQuickInfoIs(t, "module a", "");
                f.GoToMarker(t, "2");
                f.VerifyQuickInfoIs(t, "module a", "");
            }
            finally
            {
                done();
            }
        });
    }

}
