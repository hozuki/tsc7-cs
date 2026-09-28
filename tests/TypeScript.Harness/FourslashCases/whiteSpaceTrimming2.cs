// ported from: internal/fourslash/tests/whiteSpaceTrimming2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class WhiteSpaceTrimming2Case
{
    public static void TestWhiteSpaceTrimming2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("let noSubTemplate = `/*    /*1*/`;\nlet templateHead = `/*    /*2*/${1 + 2}`;\nlet templateMiddle = `/*    ${1 + 2    /*3*/}`;\nlet templateTail = `/*    ${1 + 2}    /*4*/`;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.Insert(t, "\n");
                f.GoToMarker(t, "2");
                f.Insert(t, "\n");
                f.GoToMarker(t, "3");
                f.Insert(t, "\n");
                f.GoToMarker(t, "4");
                f.Insert(t, "\n");
                f.VerifyCurrentFileContent(t, "let noSubTemplate = `/*    \n`;\nlet templateHead = `/*    \n${1 + 2}`;\nlet templateMiddle = `/*    ${1 + 2\n    }`;\nlet templateTail = `/*    ${1 + 2}    \n`;");
            }
            finally
            {
                done();
            }
        });
    }

}
