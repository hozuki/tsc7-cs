// ported from: internal/fourslash/tests/formattingInDestructuring1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormattingInDestructuring1Case
{
    public static void TestFormattingInDestructuring1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface let { }\n/*1*/var x: let         [];\n\nfunction foo() {\n    'use strict'\n/*2*/    let        [x] = [];\n/*3*/    const      [x] = [];\n/*4*/    for (let[x] = [];x < 1;) {\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.GoToMarker(t, "1");
                f.VerifyCurrentLineContent(t, "var x: let[];");
                f.GoToMarker(t, "2");
                f.VerifyCurrentLineContent(t, "    let [x] = [];");
                f.GoToMarker(t, "3");
                f.VerifyCurrentLineContent(t, "    const [x] = [];");
                f.GoToMarker(t, "4");
                f.VerifyCurrentLineContent(t, "    for (let [x] = []; x < 1;) {");
            }
            finally
            {
                done();
            }
        });
    }

}
