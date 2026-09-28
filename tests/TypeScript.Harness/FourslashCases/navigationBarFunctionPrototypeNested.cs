// ported from: internal/fourslash/tests/navigationBarFunctionPrototypeNested_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class NavigationBarFunctionPrototypeNestedCase
{
    public static void TestNavigationBarFunctionPrototypeNested(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: foo.js\nfunction A() {}\nA.B = function () {  } \nA.B.prototype.d = function () {  }  \nObject.defineProperty(A.B.prototype, \"x\", {\n    get() {}\n})\nA.prototype.D = function () {  } \nA.prototype.D.prototype.d = function () {  } "u8);
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
