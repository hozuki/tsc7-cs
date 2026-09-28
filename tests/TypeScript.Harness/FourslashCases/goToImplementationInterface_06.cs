// ported from: internal/fourslash/tests/goToImplementationInterface_06_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToImplementationInterface_06Case
{
    public static void TestGoToImplementationInterface_06(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Fo/*interface_definition*/o {\n    new (a: number): SomeOtherType;\n}\n\ninterface SomeOtherType {}\n\nlet x: Foo = [|class { constructor (a: number) {} }|];\nlet y = <Foo> [|class { constructor (a: number) {} }|];"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToImplementation(t, "interface_definition");
            }
            finally
            {
                done();
            }
        });
    }

}
