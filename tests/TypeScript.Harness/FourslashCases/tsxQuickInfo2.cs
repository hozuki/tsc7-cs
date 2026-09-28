// ported from: internal/fourslash/tests/tsxQuickInfo2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class TsxQuickInfo2Case
{
    public static void TestTsxQuickInfo2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("//@Filename: file.tsx\ndeclare namespace JSX {\n    interface Element { }\n    interface IntrinsicElements {\n        div: any\n    }\n}\nvar x1 = <di/*1*/v></di/*2*/v>\nclass MyElement {}\nvar z = <My/*3*/Element></My/*4*/Element>"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(property) JSX.IntrinsicElements.div: any", "");
                f.VerifyQuickInfoAt(t, "2", "(property) JSX.IntrinsicElements.div: any", "");
                f.VerifyQuickInfoAt(t, "3", "class MyElement", "");
                f.VerifyQuickInfoAt(t, "4", "class MyElement", "");
            }
            finally
            {
                done();
            }
        });
    }

}
