// ported from: internal/fourslash/tests/formatColonAndQMark_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormatColonAndQMarkCase
{
    public static void TestFormatColonAndQMark(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class foo {/*1*/\n    constructor (n?: number, m = 5, o?: string) { }/*2*/\n    x:number = 1?2:3;/*3*/\n}/*4*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.GoToMarker(t, "1");
                f.VerifyCurrentLineContent(t, "class foo {");
                f.GoToMarker(t, "2");
                f.VerifyCurrentLineContent(t, "    constructor(n?: number, m = 5, o?: string) { }");
                f.GoToMarker(t, "3");
                f.VerifyCurrentLineContent(t, "    x: number = 1 ? 2 : 3;");
                f.GoToMarker(t, "4");
                f.VerifyCurrentLineContent(t, "}");
            }
            finally
            {
                done();
            }
        });
    }

}
