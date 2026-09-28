// ported from: internal/fourslash/tests/goToDefinitionObjectLiteralProperties_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionObjectLiteralPropertiesCase
{
    public static void TestGoToDefinitionObjectLiteralProperties(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("var o = {\n    /*valueDefinition*/value: 0,\n    get /*getterDefinition*/getter() {return 0 },\n    set /*setterDefinition*/setter(v: number) { },\n    /*methodDefinition*/method: () => { },\n    /*es6StyleMethodDefinition*/es6StyleMethod() { }\n};\n\no./*valueReference*/value;\no./*getterReference*/getter;\no./*setterReference*/setter;\no./*methodReference*/method;\no./*es6StyleMethodReference*/es6StyleMethod;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, false, "valueReference", "getterReference", "setterReference", "methodReference", "es6StyleMethodReference");
            }
            finally
            {
                done();
            }
        });
    }

}
