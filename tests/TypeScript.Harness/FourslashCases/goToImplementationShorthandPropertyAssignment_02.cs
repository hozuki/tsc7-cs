// ported from: internal/fourslash/tests/goToImplementationShorthandPropertyAssignment_02_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToImplementationShorthandPropertyAssignment_02Case
{
    public static void TestGoToImplementationShorthandPropertyAssignment_02(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Foo {\n\t hello(): void;\n}\n\nfunction createFoo(): Foo {\n    return {\n         hello\n    };\n\n    function [|hello|]() {}\n}\n\nfunction whatever(x: Foo) {\n     x.h/*function_call*/ello();\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToImplementation(t, "function_call");
            }
            finally
            {
                done();
            }
        });
    }

}
