// ported from: internal/fourslash/tests/formattingSpacesAfterConstructor_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormattingSpacesAfterConstructorCase
{
    public static void TestFormattingSpacesAfterConstructor(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/*1*/class test { constructor                   () { } }\n/*2*/class test { constructor                   () { } }"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.GoToMarker(t, "1");
                f.VerifyCurrentLineContent(t, "class test { constructor() { } }");
                var opts319 = f.GetOptions();
                opts319!.FormatCodeSettings!.InsertSpaceAfterConstructor = TRI.TSTrue;
                f.Configure(t, opts319);
                f.FormatDocument(t, "");
                f.GoToMarker(t, "2");
                f.VerifyCurrentLineContent(t, "class test { constructor () { } }");
            }
            finally
            {
                done();
            }
        });
    }

}
