// ported from: internal/fourslash/tests/autoFormattingOnPasting_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoFormattingOnPastingCase
{
    public static void TestAutoFormattingOnPasting(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("namespace TestModule {\n/**/\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.Paste(t, " class TestClass{\nprivate   foo;\npublic testMethod( )\n{}\n}");
                f.VerifyCurrentFileContent(t, "namespace TestModule {\n    class TestClass {\n        private foo;\n        public testMethod() { }\n    }\n}");
            }
            finally
            {
                done();
            }
        });
    }

}
