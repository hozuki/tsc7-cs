// ported from: internal/fourslash/tests/tsxGoToDefinitionStatelessFunction1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class TsxGoToDefinitionStatelessFunction1Case
{
    public static void TestTsxGoToDefinitionStatelessFunction1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("//@Filename: file.tsx\n// @jsx: preserve\n// @noLib: true\ndeclare namespace JSX {\n    interface Element { }\n    interface IntrinsicElements {\n    }\n    interface ElementAttributesProperty { props; }\n}\ninterface OptionPropBag {\n    /*pt1*/propx: number\n    propString: \"hell\"\n    /*pt2*/optional?: boolean\n}\ndeclare function /*opt*/Opt(attributes: OptionPropBag): JSX.Element;\nlet opt = <[|O/*one*/pt|] />;\nlet opt1 = <[|Op/*two*/t|] [|pr/*p1*/opx|]={100} />;\nlet opt2 = <[|Op/*three*/t|] propx={100} [|opt/*p2*/ional|] />;\nlet opt3 = <[|Op/*four*/t|] wr/*p3*/ong />;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "one", "two", "three", "four", "p1", "p2");
            }
            finally
            {
                done();
            }
        });
    }

}
