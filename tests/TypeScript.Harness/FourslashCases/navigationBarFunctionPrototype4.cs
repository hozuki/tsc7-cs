// ported from: internal/fourslash/tests/navigationBarFunctionPrototype4_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class NavigationBarFunctionPrototype4Case
{
    public static void TestNavigationBarFunctionPrototype4(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: foo.js\nvar A; \nA.prototype = { };\nA.prototype = { m() {} };\nA.prototype.a = function() { };\nA.b = function() { };\n\nvar B; \nB[\"prototype\"] = { };\nB[\"prototype\"] = { m() {} };\nB[\"prototype\"][\"a\"] = function() { };\nB[\"b\"] = function() { };"u8);
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
