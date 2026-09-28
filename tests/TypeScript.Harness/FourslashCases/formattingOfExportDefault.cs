// ported from: internal/fourslash/tests/formattingOfExportDefault_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormattingOfExportDefaultCase
{
    public static void TestFormattingOfExportDefault(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("namespace Foo {\n/*1*/    export        default        class        Test { }\n}\n/*2*/export        default        function        bar() { }"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.GoToMarker(t, "1");
                f.VerifyCurrentLineContent(t, "    export default class Test { }");
                f.GoToMarker(t, "2");
                f.VerifyCurrentLineContent(t, "export default function bar() { }");
            }
            finally
            {
                done();
            }
        });
    }

}
