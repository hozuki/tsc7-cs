// ported from: internal/fourslash/tests/goToDefinitionConstructorOverloads_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionConstructorOverloadsCase
{
    public static void TestGoToDefinitionConstructorOverloads(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class ConstructorOverload {\n    [|/*constructorOverload1*/constructor|]();\n    /*constructorOverload2*/constructor(foo: string);\n    /*constructorDefinition*/constructor(foo: any)  { }\n}\n\nvar constructorOverload = new [|/*constructorOverloadReference1*/ConstructorOverload|]();\nvar constructorOverload = new [|/*constructorOverloadReference2*/ConstructorOverload|](\"foo\");\n\nclass Extended extends ConstructorOverload {\n    readonly name = \"extended\";\n}\nvar extended1 = new [|/*extendedRef1*/Extended|]();\nvar extended2 = new [|/*extendedRef2*/Extended|](\"foo\");"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "constructorOverloadReference1", "constructorOverloadReference2", "constructorOverload1", "extendedRef1", "extendedRef2");
            }
            finally
            {
                done();
            }
        });
    }

}
