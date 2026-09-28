// ported from: internal/fourslash/tests/referencesForLabel5_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ReferencesForLabel5Case
{
    public static void TestReferencesForLabel5(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/*1*/label:  while (true) {\n            if (false) /*2*/break /*3*/label;\n            function blah() {\n/*4*/label:          while (true) {\n                    if (false) /*5*/break /*6*/label;\n                }\n            }\n            if (false) /*7*/break /*8*/label;\n        }"u8);
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
