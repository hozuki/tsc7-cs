// ported from: internal/fourslash/tests/outliningSpansSwitchCases_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class OutliningSpansSwitchCasesCase
{
    public static void TestOutliningSpansSwitchCases(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("switch (undefined)[| {\n case 0:[|\n   console.log(1)\n   console.log(2)\n   break;\n   console.log(3);|]\n case 1:[|\n   break;|]\n case 2:[|\n   break;\n   console.log(3);|]\n case 3:[|\n   console.log(4);|]\n \n case 4:\n case 5:\n case 6:[|\n\n\n   console.log(5);|]\n \n case 7:[| console.log(6);|]\n\n case 8:[| [|{\n   console.log(8);\n   break;\n }|]\n console.log(8);|]\n\n default:[|\n   console.log(7);\n   console.log(8);|]\n}|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOutliningSpans(t);
            }
            finally
            {
                done();
            }
        });
    }

}
