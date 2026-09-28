// ported from: internal/fourslash/tests/jsdocDeprecated_suggestion8_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsdocDeprecated_suggestion8Case
{
    public static void TestJsdocDeprecated_suggestion8(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: first.ts\n/** @deprecated */\nexport declare function tap<T>(next: null): void;\nexport declare function tap<T>(next: T): T;\n// @Filename: second.ts\nimport { tap } from './first';\ntap"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "second.ts");
                f.VerifyNoErrors(t);
                f.VerifySuggestionDiagnostics(t, null!);
            }
            finally
            {
                done();
            }
        });
    }

}
