// ported from: internal/fourslash/tests/functionOverloadCount_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FunctionOverloadCountCase
{
    public static void TestFunctionOverloadCount(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class C1 {\n    public attr(): string;\n    public attr(i: number): string;\n    public attr(i: number, x: boolean): string;\n    public attr(i?: any, x?: any) {\n        return \"hi\";\n    }\n}\nvar i = new C1;\ni.attr(/*1*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { OverloadsCount = 3 });
            }
            finally
            {
                done();
            }
        });
    }

}
