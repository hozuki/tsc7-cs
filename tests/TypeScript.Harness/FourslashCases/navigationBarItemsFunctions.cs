// ported from: internal/fourslash/tests/navigationBarItemsFunctions_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class NavigationBarItemsFunctionsCase
{
    public static void TestNavigationBarItemsFunctions(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function foo() {\n    var x = 10;\n    function bar() {\n        var y = 10;\n        function biz() {\n            var z = 10;\n        }\n        function qux() {\n            // A function with an empty body should not be top level\n        }\n    }\n}\n\nfunction baz() {\n    var v = 10;\n}"u8);
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
