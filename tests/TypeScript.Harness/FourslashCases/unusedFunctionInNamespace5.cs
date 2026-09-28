// ported from: internal/fourslash/tests/unusedFunctionInNamespace5_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class UnusedFunctionInNamespace5Case
{
    public static void TestUnusedFunctionInNamespace5(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @noUnusedLocals: true\n// @noUnusedParameters:true\nnamespace Validation {\n    var function1 = function() {\n    }\n\n    export function function2() {\n\n    }\n\n    [| function function3() {\n        function1();\n    }\n\n    function function4() {\n\n    }\n\n    export let a = function3; |]\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyRangeAfterCodeFix(t, "function function3() {\n        function1();\n    }\n\n    export let a = function3;", false, 0, 0);
            }
            finally
            {
                done();
            }
        });
    }

}
