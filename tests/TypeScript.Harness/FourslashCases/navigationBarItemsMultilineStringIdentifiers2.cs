// ported from: internal/fourslash/tests/navigationBarItemsMultilineStringIdentifiers2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class NavigationBarItemsMultilineStringIdentifiers2Case
{
    public static void TestNavigationBarItemsMultilineStringIdentifiers2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function f(p1: () => any, p2: string) { }\nf(() => { }, `line1\\\nline2\\\nline3`);\n\nclass c1 {\n    const a = ' ''line1\\\n        line2';\n}\n\nf(() => { }, `unterminated backtick 1\nunterminated backtick 2\nunterminated backtick 3"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineDocumentSymbol(t);
            }
            finally
            {
                done();
            }
        });
    }

}
