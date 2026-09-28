// ported from: internal/fourslash/tests/goToDefinitionDecoratorOverloads_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionDecoratorOverloadsCase
{
    public static void TestGoToDefinitionDecoratorOverloads(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Target: ES6\n// @experimentaldecorators: true\nasync function f() {}\n\nfunction /*defDecString*/dec(target: any, propertyKey: string): void;\nfunction /*defDecSymbol*/dec(target: any, propertyKey: symbol): void;\nfunction dec(target: any, propertyKey: string | symbol) {}\n\ndeclare const s: symbol;\nclass C {\n    @[|/*useDecString*/dec|] f() {}\n    @[|/*useDecSymbol*/dec|] [s]() {}\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "useDecString", "useDecSymbol");
            }
            finally
            {
                done();
            }
        });
    }

}
