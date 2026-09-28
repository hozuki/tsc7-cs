// ported from: internal/fourslash/tests/quickInfoDisplayPartsEnum2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoDisplayPartsEnum2Case
{
    public static void TestQuickInfoDisplayPartsEnum2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("enum /*1*/E {\n    /*2*/\"e1\",\n    /*3*/'e2' = 10,\n    /*4*/\"e3\"\n}\nvar /*5*/eInstance: /*6*/E;\n/*7*/eInstance = /*8*/E./*9*/e1;\n/*10*/eInstance = /*11*/E./*12*/e2;\n/*13*/eInstance = /*14*/E./*15*/e3;\nconst enum /*16*/constE {\n    /*17*/\"e1\",\n    /*18*/'e2' = 10,\n    /*19*/\"e3\"\n}\nvar /*20*/eInstance1: /*21*/constE;\n/*22*/eInstance1 = /*23*/constE./*24*/e1;\n/*25*/eInstance1 = /*26*/constE./*27*/e2;\n/*28*/eInstance1 = /*29*/constE./*30*/e3;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHover(t);
            }
            finally
            {
                done();
            }
        });
    }

}
