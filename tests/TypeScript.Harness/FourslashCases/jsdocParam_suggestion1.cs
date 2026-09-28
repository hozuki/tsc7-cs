// ported from: internal/fourslash/tests/jsdocParam_suggestion1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsdocParam_suggestion1Case
{
    public static void TestJsdocParam_suggestion1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: a.ts\n/**\n * @param options - whatever\n * @param options.zone - equally bad\n */\ndeclare function bad(options: any): void\n\n/**\n * @param {number} obtuse\n */\nfunction worse(): void {\n    arguments\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "a.ts");
                f.VerifySuggestionDiagnostics(t, null!);
            }
            finally
            {
                done();
            }
        });
    }

}
