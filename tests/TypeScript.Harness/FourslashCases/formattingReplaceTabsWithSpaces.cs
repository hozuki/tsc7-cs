// ported from: internal/fourslash/tests/formattingReplaceTabsWithSpaces_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormattingReplaceTabsWithSpacesCase
{
    public static void TestFormattingReplaceTabsWithSpaces(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("namespace Foo {\n/*1*/\t\t\t\tclass Test { }\n/*2*/\t\t\tclass Test { }\n/*3*/class Test { }\n/*4*/\t\t\t class Test { }\n/*5*/   class Test { }\n/*6*/    class Test { }\n/*7*/     class Test { }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.GoToMarker(t, "1");
                f.VerifyCurrentLineContent(t, "    class Test { }");
                f.GoToMarker(t, "2");
                f.VerifyCurrentLineContent(t, "    class Test { }");
                f.GoToMarker(t, "3");
                f.VerifyCurrentLineContent(t, "    class Test { }");
                f.GoToMarker(t, "4");
                f.VerifyCurrentLineContent(t, "    class Test { }");
                f.GoToMarker(t, "5");
                f.VerifyCurrentLineContent(t, "    class Test { }");
                f.GoToMarker(t, "6");
                f.VerifyCurrentLineContent(t, "    class Test { }");
                f.GoToMarker(t, "7");
                f.VerifyCurrentLineContent(t, "    class Test { }");
            }
            finally
            {
                done();
            }
        });
    }

}
