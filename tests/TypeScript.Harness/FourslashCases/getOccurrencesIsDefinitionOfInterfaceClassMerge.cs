// ported from: internal/fourslash/tests/getOccurrencesIsDefinitionOfInterfaceClassMerge_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetOccurrencesIsDefinitionOfInterfaceClassMergeCase
{
    public static void TestGetOccurrencesIsDefinitionOfInterfaceClassMerge(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/*1*/interface /*2*/Numbers {\n    p: number;\n}\n/*3*/interface /*4*/Numbers {\n    m: number;\n}\n/*5*/class /*6*/Numbers {\n    f(n: number) {\n        return this.p + this.m + n;\n    }\n}\nlet i: /*7*/Numbers = new /*8*/Numbers();\nlet x = i.f(i.p + i.m);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1", "2", "3", "4", "5", "6", "7", "8");
            }
            finally
            {
                done();
            }
        });
    }

}
