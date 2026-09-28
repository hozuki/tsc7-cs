// ported from: internal/fourslash/tests/goToDefinitionAcrossMultipleProjects_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionAcrossMultipleProjectsCase
{
    public static void TestGoToDefinitionAcrossMultipleProjects(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("//@Filename: a.ts\nvar /*def1*/x: number;\n//@Filename: b.ts\nvar /*def2*/x: number;\n//@Filename: c.ts\nvar /*def3*/x: number;\n//@Filename: d.ts\nvar /*def4*/x: number;\n//@Filename: e.ts\n/// <reference path=\"a.ts\" />\n/// <reference path=\"b.ts\" />\n/// <reference path=\"c.ts\" />\n/// <reference path=\"d.ts\" />\n[|/*use*/x|]++;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "use");
            }
            finally
            {
                done();
            }
        });
    }

}
