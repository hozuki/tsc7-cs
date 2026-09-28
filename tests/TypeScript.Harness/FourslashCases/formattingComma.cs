// ported from: internal/fourslash/tests/formattingComma_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormattingCommaCase
{
    public static void TestFormattingComma(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("var x = [1 , 2];/*x*/\nvar y = ( 1  , 2 );/*y*/\nvar z1 = 1 , zz = 2;/*z1*/\nvar z2 = {\n    x: 1 ,/*z2*/\n    y: 2\n};\nvar z3 = (\n    () => { }  ,/*z3*/\n    () => { }\n    );\nvar z4 = [\n    () => { } ,/*z4*/\n    () => { }\n];\nvar z5 = {\n    x: () => { } ,/*z5*/\n    y: () => { }\n}; "u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.GoToMarker(t, "x");
                f.VerifyCurrentLineContent(t, "var x = [1, 2];");
                f.GoToMarker(t, "y");
                f.VerifyCurrentLineContent(t, "var y = (1, 2);");
                f.GoToMarker(t, "z1");
                f.VerifyCurrentLineContent(t, "var z1 = 1, zz = 2;");
                f.GoToMarker(t, "z2");
                f.VerifyCurrentLineContent(t, "    x: 1,");
                f.GoToMarker(t, "z3");
                f.VerifyCurrentLineContent(t, "    () => { },");
                f.GoToMarker(t, "z4");
                f.VerifyCurrentLineContent(t, "    () => { },");
                f.GoToMarker(t, "z5");
                f.VerifyCurrentLineContent(t, "    x: () => { },");
            }
            finally
            {
                done();
            }
        });
    }

}
