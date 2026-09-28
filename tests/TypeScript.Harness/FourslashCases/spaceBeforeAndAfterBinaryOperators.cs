// ported from: internal/fourslash/tests/spaceBeforeAndAfterBinaryOperators_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SpaceBeforeAndAfterBinaryOperatorsCase
{
    public static void TestSpaceBeforeAndAfterBinaryOperators(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("let i = 0;\n/*1*/(i++,i++);\n/*2*/(i++,++i);\n/*3*/(1,2);\n/*4*/(i++,2);\n/*5*/(i++,i++,++i,i--,2);\nlet s = 'foo';\n/*6*/for (var i = 0,ii = 2; i < s.length; ii++,i++) {\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.GoToMarker(t, "1");
                f.VerifyCurrentLineContent(t, "(i++, i++);");
                f.GoToMarker(t, "2");
                f.VerifyCurrentLineContent(t, "(i++, ++i);");
                f.GoToMarker(t, "3");
                f.VerifyCurrentLineContent(t, "(1, 2);");
                f.GoToMarker(t, "4");
                f.VerifyCurrentLineContent(t, "(i++, 2);");
                f.GoToMarker(t, "5");
                f.VerifyCurrentLineContent(t, "(i++, i++, ++i, i--, 2);");
                f.GoToMarker(t, "6");
                f.VerifyCurrentLineContent(t, "for (var i = 0, ii = 2; i < s.length; ii++, i++) {");
            }
            finally
            {
                done();
            }
        });
    }

}
