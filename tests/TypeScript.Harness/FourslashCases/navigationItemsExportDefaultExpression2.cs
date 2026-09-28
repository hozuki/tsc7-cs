// ported from: internal/fourslash/tests/navigationItemsExportDefaultExpression2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class NavigationItemsExportDefaultExpression2Case
{
    public static void TestNavigationItemsExportDefaultExpression2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("export const foo = {\n  foo: {},\n};\n\nexport default {\n  foo: {},\n};\n\nexport default {\n  foo: {},\n};\n\ntype Type = typeof foo;\n\nexport default {\n  foo: {},\n} as Type;\n\nexport default {\n  foo: {},\n} satisfies Type;\n\nexport default (class {\n  prop = 42;\n});\n\nexport default (class Cls {\n  prop = 42;\n});"u8);
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
