// ported from: internal/fourslash/tests/commentsBlocks_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CommentsBlocksCase
{
    public static void TestCommentsBlocks(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/*1*/// 1\nvar x,\n    /*2*/// 2\n    y,\n/*3*/     /* %3 */\n    z;\n\n/*4*/ // 4\nswitch (x) {\n/*5*/     // 5\n    case 1:\n/*6*/         // 6\n        break;\n/*7*/     // 7\n    case 2:\n/*8*/     // 8\n}\n\n/*9*/ // 9\nif (true)\n/*10*/     // 10\n    ;\n/*11*/ // 11\nelse {\n/*12*/     // 12\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.GoToMarker(t, "1");
                f.VerifyCurrentLineContent(t, "// 1");
                f.GoToMarker(t, "2");
                f.VerifyCurrentLineContent(t, "    // 2");
                f.GoToMarker(t, "3");
                f.VerifyCurrentLineContent(t, "    /* %3 */");
                f.GoToMarker(t, "4");
                f.VerifyCurrentLineContent(t, "// 4");
                f.GoToMarker(t, "5");
                f.VerifyCurrentLineContent(t, "    // 5");
                f.GoToMarker(t, "6");
                f.VerifyCurrentLineContent(t, "        // 6");
                f.GoToMarker(t, "7");
                f.VerifyCurrentLineContent(t, "    // 7");
                f.GoToMarker(t, "8");
                f.VerifyCurrentLineContent(t, "    // 8");
                f.GoToMarker(t, "9");
                f.VerifyCurrentLineContent(t, "// 9");
                f.GoToMarker(t, "10");
                f.VerifyCurrentLineContent(t, "    // 10");
                f.GoToMarker(t, "11");
                f.VerifyCurrentLineContent(t, "// 11");
                f.GoToMarker(t, "12");
                f.VerifyCurrentLineContent(t, "    // 12");
            }
            finally
            {
                done();
            }
        });
    }

}
