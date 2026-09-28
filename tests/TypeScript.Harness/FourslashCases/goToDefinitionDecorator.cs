// ported from: internal/fourslash/tests/goToDefinitionDecorator_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionDecoratorCase
{
    public static void TestGoToDefinitionDecorator(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: b.ts\n@[|/*decoratorUse*/decorator|]\nclass C {\n    @[|decora/*decoratorFactoryUse*/torFactory|](a, \"22\", true)\n    method() {}\n}\n// @Filename: a.ts\nfunction /*decoratorDefinition*/decorator(target) {\n    return target;\n}\nfunction /*decoratorFactoryDefinition*/decoratorFactory(...args) {\n    return target => target;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "decoratorUse", "decoratorFactoryUse");
            }
            finally
            {
                done();
            }
        });
    }

}
