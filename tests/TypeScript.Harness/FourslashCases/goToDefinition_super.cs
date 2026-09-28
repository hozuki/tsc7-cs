// ported from: internal/fourslash/tests/goToDefinition_super_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinition_superCase
{
    public static void TestGoToDefinition_super(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class A {\n    /*ctr*/constructor() {}\n    x() {}\n}\nclass /*B*/B extends A {}\nclass C extends B {\n    constructor() {\n        [|/*super*/super|]();\n    }\n    method() {\n        [|/*superExpression*/super|].x();\n    }\n}\nclass D {\n    constructor() {\n        /*superBroken*/super();\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "super", "superExpression", "superBroken");
            }
            finally
            {
                done();
            }
        });
    }

}
