// ported from: internal/fourslash/tests/goToDefinitionLabels_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionLabelsCase
{
    public static void TestGoToDefinitionLabels(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/*label1Definition*/label1: while (true) {\n    /*label2Definition*/label2: while (true) {\n        break [|/*1*/label1|];\n        continue [|/*2*/label2|];\n        () => { break [|/*3*/label1|]; }\n        continue /*4*/unknownLabel;\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "1", "2", "3", "4");
            }
            finally
            {
                done();
            }
        });
    }

}
