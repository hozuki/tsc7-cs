// ported from: internal/fourslash/tests/quickInfoDisplayPartsParameters_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoDisplayPartsParametersCase
{
    public static void TestQuickInfoDisplayPartsParameters(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/** @return *crunch* */\nfunction /*1*/foo(/*2*/param: string, /*3*/optionalParam?: string, /*4*/paramWithInitializer = \"hello\", .../*5*/restParam: string[]) {\n    /*6*/param = \"Hello\";\n    /*7*/optionalParam = \"World\";\n    /*8*/paramWithInitializer = \"Hello\";\n    /*9*/restParam[0] = \"World\";\n}"u8);
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
