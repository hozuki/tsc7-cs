// ported from: internal/fourslash/tests/quickInfoRootSymbolJSDocAggregation_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoRootSymbolJSDocAggregationCase
{
    public static void TestQuickInfoRootSymbolJSDocAggregation(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\ndeclare const distinct: {\n    /** first */\n    a: number;\n} & {\n    /** second */\n    a: number;\n};\n\ndeclare const duplicate: {\n    /** same */\n    a: number;\n} & {\n    /** same */\n    a: number;\n} & {\n    /** third */\n    a: number;\n};\n\ndeclare const mixed: {\n    /** first */\n    a: number;\n} & {\n    /** second */\n    a: number;\n} & {\n    /** first */\n    a: number;\n};\n\ndistinct./*distinct*/a\nduplicate./*duplicate*/a\nmixed./*mixed*/a\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "distinct", "(property) a: number", "first\nsecond");
                f.VerifyQuickInfoAt(t, "duplicate", "(property) a: number", "same\nthird");
                f.VerifyQuickInfoAt(t, "mixed", "(property) a: number", "first\nsecond");
            }
            finally
            {
                done();
            }
        });
    }

}
