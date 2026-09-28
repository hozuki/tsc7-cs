// ported from: internal/fourslash/tests/tsxQuickInfo3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class TsxQuickInfo3Case
{
    public static void TestTsxQuickInfo3(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("//@Filename: file.tsx\n// @jsx: preserve\n// @noLib: true\ninterface OptionProp {\n    propx: 2\n}\nclass Opt extends React.Component<OptionProp, {}> {\n    render() {\n        return <div>Hello</div>;\n    }\n}\nconst obj1: OptionProp = {\n    propx: 2\n}\nlet y1 = <O/*1*/pt pro/*2*/px={2} />;\nlet y2 = <Opt {...ob/*3*/j1} />;\nlet y2 = <Opt {...obj1} pr/*4*/opx />;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "class Opt", "");
                f.VerifyQuickInfoAt(t, "2", "(property) propx: number", "");
                f.VerifyQuickInfoAt(t, "3", "const obj1: OptionProp", "");
                f.VerifyQuickInfoAt(t, "4", "(property) propx: true", "");
            }
            finally
            {
                done();
            }
        });
    }

}
