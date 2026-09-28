// ported from: internal/fourslash/tests/referencesForEnums_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ReferencesForEnumsCase
{
    public static void TestReferencesForEnums(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("enum E {\n    /*1*/value1 = 1,\n    /*2*/\"/*3*/value2\" = /*4*/value1,\n    /*5*/111 = 11\n}\n\nE./*6*/value1;\nE[\"/*7*/value2\"];\nE./*8*/value2;\nE[/*9*/111];"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1", "2", "3", "4", "5", "6", "7", "8", "9");
            }
            finally
            {
                done();
            }
        });
    }

}
