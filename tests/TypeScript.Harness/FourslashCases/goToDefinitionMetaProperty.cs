// ported from: internal/fourslash/tests/goToDefinitionMetaProperty_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionMetaPropertyCase
{
    public static void TestGoToDefinitionMetaProperty(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /a.ts\nim/*1*/port.met/*2*/a;\nfunction /*functionDefinition*/f() { n/*3*/ew.[|t/*4*/arget|]; }\n// @Filename: /b.ts\nim/*5*/port.m;\nclass /*classDefinition*/c { constructor() { n/*6*/ew.[|t/*7*/arget|]; } }"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "1", "2", "3", "4", "5", "6", "7");
            }
            finally
            {
                done();
            }
        });
    }

}
