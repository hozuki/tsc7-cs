// ported from: internal/fourslash/tests/goToImplementationNamespace_03_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToImplementationNamespace_03Case
{
    public static void TestGoToImplementationNamespace_03(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("namespace Foo {\n    export interface Bar {\n        hello(): void;\n    }\n\n    class [|BarImpl|] implements Bar {\n        hello() {}\n    }\n}\n\nclass [|Baz|] implements Foo.Bar {\n    hello() {}\n}\n\nvar someVar1 : Foo.Bar = [|{ hello: () => {/**1*/} }|];\n\nvar someVar2 = <Foo.Bar> [|{ hello: () => {/**2*/} }|];\n\nfunction whatever(x: Foo.Ba/*reference*/r) {\n\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToImplementation(t, "reference");
            }
            finally
            {
                done();
            }
        });
    }

}
