// ported from: internal/fourslash/tests/constructorBraceFormatting_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ConstructorBraceFormattingCase
{
    public static void TestConstructorBraceFormatting(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class X {\n    constructor () {}/*target*/\n /**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.Insert(t, "}");
                f.GoToMarker(t, "target");
                f.VerifyCurrentLineContent(t, "    constructor() { }");
            }
            finally
            {
                done();
            }
        });
    }

}
