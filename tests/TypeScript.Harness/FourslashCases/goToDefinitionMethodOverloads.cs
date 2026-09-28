// ported from: internal/fourslash/tests/goToDefinitionMethodOverloads_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionMethodOverloadsCase
{
    public static void TestGoToDefinitionMethodOverloads(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class MethodOverload {\n    static [|/*staticMethodOverload1*/method|]();\n    static /*staticMethodOverload2*/method(foo: string);\n    static /*staticMethodDefinition*/method(foo?: any) { }\n    public [|/*instanceMethodOverload1*/method|](): any;\n    public /*instanceMethodOverload2*/method(foo: string);\n    public /*instanceMethodDefinition*/method(foo?: any) { return \"foo\" }\n}\n// static method\nMethodOverload.[|/*staticMethodReference1*/method|]();\nMethodOverload.[|/*staticMethodReference2*/method|](\"123\");\n// instance method\nvar methodOverload = new MethodOverload();\nmethodOverload.[|/*instanceMethodReference1*/method|]();\nmethodOverload.[|/*instanceMethodReference2*/method|](\"456\");"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "staticMethodReference1", "staticMethodReference2", "instanceMethodReference1", "instanceMethodReference2", "staticMethodOverload1", "instanceMethodOverload1");
            }
            finally
            {
                done();
            }
        });
    }

}
