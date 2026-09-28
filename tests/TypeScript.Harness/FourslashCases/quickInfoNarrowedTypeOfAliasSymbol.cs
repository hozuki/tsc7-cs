// ported from: internal/fourslash/tests/quickInfoNarrowedTypeOfAliasSymbol_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoNarrowedTypeOfAliasSymbolCase
{
    public static void TestQuickInfoNarrowedTypeOfAliasSymbol(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: true\n// @Filename: modules.ts\nexport declare const someEnv: string | undefined;\n// @Filename: app.ts\nimport { someEnv } from \"./modules\";\ndeclare function isString(v: any): v is string;\n\nif (isString(someEnv)) {\n  someEnv/*1*/.charAt(0);\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "app.ts");
                f.GoToMarker(t, "1");
                f.VerifyQuickInfoIs(t, "(alias) const someEnv: string\nimport someEnv", "");
            }
            finally
            {
                done();
            }
        });
    }

}
