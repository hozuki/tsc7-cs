// ported from: internal/fourslash/tests/commentsLinePreservation_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CommentsLinePreservationCase
{
    public static void TestCommentsLinePreservation(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/** This is firstLine\n  * This is second Line\n  * \n  * This is fourth Line\n  */\nvar /*a*/a: string;\n/** \n  * This is firstLine\n  * This is second Line\n  * \n  * This is fourth Line\n  */\nvar /*b*/b: string;\n/** \n  * This is firstLine\n  * This is second Line\n  * \n  * This is fourth Line\n  *\n  */\nvar /*c*/c: string;\n/** \n  * This is firstLine\n  * This is second Line\n  * @param param\n  * @random tag This should be third line\n  */\nfunction /*d*/d(param: string) { /*1*/param = \"hello\"; }\n/** \n  * This is firstLine\n  * This is second Line\n  * @param param\n  */\nfunction /*e*/e(param: string) { /*2*/param = \"hello\"; }\n/** \n  * This is firstLine\n  * This is second Line\n  * @param param1 first line of param\n  *\n  *  param information third line\n  * @random tag This should be third line\n  */\nfunction /*f*/f(param1: string) { /*3*/param1 = \"hello\"; }\n/** \n  * This is firstLine\n  * This is second Line\n  * @param param1\n  *\n  *  param information first line\n  * @random tag This should be third line\n  */\nfunction /*g*/g(param1: string) { /*4*/param1 = \"hello\"; }\n/** \n  * This is firstLine\n  * This is second Line\n  * @param param1\n  *\n  *  param information first line\n  *\n  *  param information third line\n  * @random tag This should be third line\n  */\nfunction /*h*/h(param1: string) { /*5*/param1 = \"hello\"; }\n/** \n  * This is firstLine\n  * This is second Line\n  * @param param1\n  *\n  *  param information first line\n  *\n  *  param information third line\n  *\n  */\nfunction /*i*/i(param1: string) { /*6*/param1 = \"hello\"; }\n/** \n  * This is firstLine\n  * This is second Line\n  * @param param1\n  *\n  *  param information first line\n  *\n  *  param information third line\n  */\nfunction /*j*/j(param1: string) { /*7*/param1 = \"hello\"; }\n/** \n  * This is firstLine\n  * This is second Line\n  * @param param1 hello   @randomtag \n  *\n  *  random information first line\n  *\n  *  random information third line\n  */\nfunction /*k*/k(param1: string) { /*8*/param1 = \"hello\"; }\n/** \n  * This is firstLine\n  * This is second Line\n  * @param param1 first Line text\n  *\n  * @param param1 \n  *\n  * blank line that shouldnt be shown when starting this \n  * second time information about the param again\n  */\nfunction /*l*/l(param1: string) { /*9*/param1 = \"hello\"; }\n     /** \n       * This is firstLine\n This is second Line\n [1]: third * line\n @param param1 first Line text\n second line text\n */\nfunction /*m*/m(param1: string) { /*10*/param1 = \"hello\"; }"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "a", "var a: string", "This is firstLine\nThis is second Line\n\nThis is fourth Line");
                f.VerifyQuickInfoAt(t, "b", "var b: string", "This is firstLine\nThis is second Line\n\nThis is fourth Line");
                f.VerifyQuickInfoAt(t, "c", "var c: string", "This is firstLine\nThis is second Line\n\nThis is fourth Line");
                f.VerifyQuickInfoAt(t, "d", "function d(param: string): void", "This is firstLine\nThis is second Line");
                f.VerifyQuickInfoAt(t, "1", "(parameter) param: string", "");
                f.VerifyQuickInfoAt(t, "e", "function e(param: string): void", "This is firstLine\nThis is second Line");
                f.VerifyQuickInfoAt(t, "2", "(parameter) param: string", "");
                f.VerifyQuickInfoAt(t, "f", "function f(param1: string): void", "This is firstLine\nThis is second Line");
                f.VerifyQuickInfoAt(t, "3", "(parameter) param1: string", "first line of param\n\nparam information third line");
                f.VerifyQuickInfoAt(t, "g", "function g(param1: string): void", "This is firstLine\nThis is second Line");
                f.VerifyQuickInfoAt(t, "4", "(parameter) param1: string", " param information first line");
                f.VerifyQuickInfoAt(t, "h", "function h(param1: string): void", "This is firstLine\nThis is second Line");
                f.VerifyQuickInfoAt(t, "5", "(parameter) param1: string", " param information first line\n\n param information third line");
                f.VerifyQuickInfoAt(t, "i", "function i(param1: string): void", "This is firstLine\nThis is second Line");
                f.VerifyQuickInfoAt(t, "6", "(parameter) param1: string", " param information first line\n\n param information third line");
                f.VerifyQuickInfoAt(t, "j", "function j(param1: string): void", "This is firstLine\nThis is second Line");
                f.VerifyQuickInfoAt(t, "7", "(parameter) param1: string", " param information first line\n\n param information third line");
                f.VerifyQuickInfoAt(t, "k", "function k(param1: string): void", "This is firstLine\nThis is second Line");
                f.VerifyQuickInfoAt(t, "8", "(parameter) param1: string", "hello");
                f.VerifyQuickInfoAt(t, "l", "function l(param1: string): void", "This is firstLine\nThis is second Line");
                f.VerifyQuickInfoAt(t, "9", "(parameter) param1: string", "first Line text\nblank line that shouldnt be shown when starting this \nsecond time information about the param again");
                f.VerifyQuickInfoAt(t, "m", "function m(param1: string): void", "This is firstLine\nThis is second Line\n[1]: third * line");
                f.VerifyQuickInfoAt(t, "10", "(parameter) param1: string", "first Line text\nsecond line text");
            }
            finally
            {
                done();
            }
        });
    }

}
