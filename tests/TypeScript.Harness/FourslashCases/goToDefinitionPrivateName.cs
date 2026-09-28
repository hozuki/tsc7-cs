// ported from: internal/fourslash/tests/goToDefinitionPrivateName_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionPrivateNameCase
{
    public static void TestGoToDefinitionPrivateName(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class A {\n    [|/*pnMethodDecl*/#method|]() { }\n    [|/*pnFieldDecl*/#foo|] = 3;\n    get [|/*pnPropGetDecl*/#prop|]() { return \"\"; }\n    set [|/*pnPropSetDecl*/#prop|](value: string) {  }\n    constructor() {\n        this.[|/*pnFieldUse*/#foo|]\n        this.[|/*pnMethodUse*/#method|]\n        this.[|/*pnPropUse*/#prop|]\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "pnFieldUse", "pnMethodUse", "pnPropUse");
            }
            finally
            {
                done();
            }
        });
    }

}
