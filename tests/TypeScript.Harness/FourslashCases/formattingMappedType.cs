// ported from: internal/fourslash/tests/formattingMappedType_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormattingMappedTypeCase
{
    public static void TestFormattingMappedType(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/*generic*/type t  < T  > =   {\n/*map*/   [   P   in   keyof    T  ]   :   T  [  P  ]\n};"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.GoToMarker(t, "generic");
                f.VerifyCurrentLineContent(t, "type t<T> = {");
                f.GoToMarker(t, "map");
                f.VerifyCurrentLineContent(t, "    [P in keyof T]: T[P]");
            }
            finally
            {
                done();
            }
        });
    }

}
