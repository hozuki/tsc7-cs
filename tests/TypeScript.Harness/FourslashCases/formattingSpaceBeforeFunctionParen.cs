// ported from: internal/fourslash/tests/formattingSpaceBeforeFunctionParen_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormattingSpaceBeforeFunctionParenCase
{
    public static void TestFormattingSpaceBeforeFunctionParen(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/*1*/function foo() { }\n/*2*/function boo  () { }\n/*3*/var bar = function foo() { };\n/*4*/var foo = { bar() { } };\n/*5*/function tmpl <T> () { }\n/*6*/var f = function*() { };\n/*7*/function* g () { }"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                var opts333 = f.GetOptions();
                opts333!.FormatCodeSettings!.InsertSpaceBeforeFunctionParenthesis = TRI.TSTrue;
                f.Configure(t, opts333);
                var opts414 = f.GetOptions();
                opts414!.FormatCodeSettings!.InsertSpaceAfterFunctionKeywordForAnonymousFunctions = TRI.TSFalse;
                f.Configure(t, opts414);
                f.FormatDocument(t, "");
                f.GoToMarker(t, "1");
                f.VerifyCurrentLineContent(t, "function foo () { }");
                f.GoToMarker(t, "2");
                f.VerifyCurrentLineContent(t, "function boo () { }");
                f.GoToMarker(t, "3");
                f.VerifyCurrentLineContent(t, "var bar = function foo () { };");
                f.GoToMarker(t, "4");
                f.VerifyCurrentLineContent(t, "var foo = { bar () { } };");
                f.GoToMarker(t, "5");
                f.VerifyCurrentLineContent(t, "function tmpl<T> () { }");
                f.GoToMarker(t, "6");
                f.VerifyCurrentLineContent(t, "var f = function*() { };");
                f.GoToMarker(t, "7");
                f.VerifyCurrentLineContent(t, "function* g () { }");
            }
            finally
            {
                done();
            }
        });
    }

}
