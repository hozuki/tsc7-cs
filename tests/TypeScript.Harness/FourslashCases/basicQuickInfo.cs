// ported from: internal/fourslash/tests/basicQuickInfo_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class BasicQuickInfoCase
{
    public static void TestBasicQuickInfo(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\n/**\n * Some var\n */\nvar someVar/*1*/ = 123;\n\n/**\n * Other var\n * See {@link someVar}\n */\nvar otherVar/*2*/ = someVar;\n\nclass Foo/*3*/ {\n\t#bar: string;\n}\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "var someVar: number", "Some var");
                f.VerifyQuickInfoAt(t, "2", "var otherVar: number", "Other var\nSee [someVar](file:///basicQuickInfo.ts#5,5-5,12)");
            }
            finally
            {
                done();
            }
        });
    }

}
