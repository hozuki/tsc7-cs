// ported from: internal/fourslash/tests/classExtendsInterfaceSigHelp1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ClassExtendsInterfaceSigHelp1Case
{
    public static void TestClassExtendsInterfaceSigHelp1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class C {\n    public foo(x: string);\n    public foo(x: number);\n    public foo(x: any) { return x; }\n}\ninterface I extends C {\n    other(x: any): any;\n}\nvar i: I;\ni.foo(/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { ParameterSpan = "x: string", OverloadsCount = 2 });
            }
            finally
            {
                done();
            }
        });
    }

}
