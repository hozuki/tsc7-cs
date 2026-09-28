// ported from: internal/fourslash/tests/goToDefinitionFunctionOverloadsInClass_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionFunctionOverloadsInClassCase
{
    public static void TestGoToDefinitionFunctionOverloadsInClass(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class clsInOverload {\n    static fnOverload();\n    static [|/*staticFunctionOverload*/fnOverload|](foo: string);\n    static /*staticFunctionOverloadDefinition*/fnOverload(foo: any) { }\n    public [|/*functionOverload*/fnOverload|](): any;\n    public fnOverload(foo: string);\n    public /*functionOverloadDefinition*/fnOverload(foo: any) { return \"foo\" }\n\n    constructor() { }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "staticFunctionOverload", "functionOverload");
            }
            finally
            {
                done();
            }
        });
    }

}
