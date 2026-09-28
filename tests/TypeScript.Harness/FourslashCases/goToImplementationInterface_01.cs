// ported from: internal/fourslash/tests/goToImplementationInterface_01_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToImplementationInterface_01Case
{
    public static void TestGoToImplementationInterface_01(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Fo/*interface_definition*/o { hello(): void }\n\nclass [|SuperBar|] implements Foo {\n    hello () {}\n}\n\nabstract class [|AbstractBar|] implements Foo {\n    abstract hello (): void;\n}\n\nclass [|Bar|] extends SuperBar {\n}\n\nclass [|NotAbstractBar|] extends AbstractBar {\n    hello () {}\n}\n\nvar x = new SuperBar();\nvar y: SuperBar = new SuperBar();\nvar z: AbstractBar = new NotAbstractBar();"u8);
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
