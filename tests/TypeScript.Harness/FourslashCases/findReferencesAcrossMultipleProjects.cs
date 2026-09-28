// ported from: internal/fourslash/tests/findReferencesAcrossMultipleProjects_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindReferencesAcrossMultipleProjectsCase
{
    public static void TestFindReferencesAcrossMultipleProjects(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("//@Filename: a.ts\n/*1*/var /*2*/x: number;\n//@Filename: b.ts\n/// <reference path=\"a.ts\" />\n/*3*/x++;\n//@Filename: c.ts\n/// <reference path=\"a.ts\" />\n/*4*/x++;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1", "2", "3", "4");
            }
            finally
            {
                done();
            }
        });
    }

}
