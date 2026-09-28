// ported from: internal/fourslash/tests/quickInfoEnumMembersAcceptNonAsciiStrings_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoEnumMembersAcceptNonAsciiStringsCase
{
    public static void TestQuickInfoEnumMembersAcceptNonAsciiStrings(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("enum Demo {\n    /*Emoji*/Emoji = '\U0001F34E',\n    /*Hebrew*/Hebrew = '\u05EA\u05E4\u05D5\u05D7',\n    /*Chinese*/Chinese = '\u82F9\u679C',\n    /*Japanese*/Japanese = '\u308A\u3093\u3054',\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "Emoji", "(enum member) Demo.Emoji = \"\ud83c\udf4e\"", "");
                f.VerifyQuickInfoAt(t, "Hebrew", "(enum member) Demo.Hebrew = \"\u05ea\u05e4\u05d5\u05d7\"", "");
                f.VerifyQuickInfoAt(t, "Chinese", "(enum member) Demo.Chinese = \"\u82f9\u679c\"", "");
                f.VerifyQuickInfoAt(t, "Japanese", "(enum member) Demo.Japanese = \"\u308a\u3093\u3054\"", "");
            }
            finally
            {
                done();
            }
        });
    }

}
