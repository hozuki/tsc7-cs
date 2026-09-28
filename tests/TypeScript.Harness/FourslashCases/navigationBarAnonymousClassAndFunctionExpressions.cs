// ported from: internal/fourslash/tests/navigationBarAnonymousClassAndFunctionExpressions_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class NavigationBarAnonymousClassAndFunctionExpressionsCase
{
    public static void TestNavigationBarAnonymousClassAndFunctionExpressions(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("global.cls = class { };\n(function() {\n    const x = () => {\n        // Presence of inner function causes x to be a top-level function.\n        function xx() {}\n    };\n    const y = {\n        // This is not a top-level function (contains nothing, but shows up in childItems of its parent.)\n        foo: function() {}\n    };\n    (function nest() {\n        function moreNest() {}\n    })();\n})();\n(function() { // Different anonymous functions are not merged\n    // These will only show up as childItems.\n    function z() {}\n    console.log(function() {})\n    describe(\"this\", 'function', `is a function`, `with template literal ${\"a\"}`, () => {});\n    [].map(() => {});\n})\n(function classes() {\n    // Classes show up in top-level regardless of whether they have names or inner declarations.\n    const cls2 = class { };\n    console.log(class cls3 {});\n    (class { });\n})"u8);
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
