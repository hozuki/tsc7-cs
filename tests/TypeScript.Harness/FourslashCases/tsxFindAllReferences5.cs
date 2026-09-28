// ported from: internal/fourslash/tests/tsxFindAllReferences5_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class TsxFindAllReferences5Case
{
    public static void TestTsxFindAllReferences5(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("//@Filename: file.tsx\n// @jsx: preserve\n// @noLib: true\ndeclare namespace JSX {\n    interface Element { }\n    interface IntrinsicElements {\n    }\n    interface ElementAttributesProperty { props; }\n}\ninterface OptionPropBag {\n    propx: number\n    propString: string\n    optional?: boolean\n}\n/*1*/declare function /*2*/Opt(attributes: OptionPropBag): JSX.Element;\nlet opt = /*3*/</*4*/Opt />;\nlet opt1 = /*5*/</*6*/Opt propx={100} propString />;\nlet opt2 = /*7*/</*8*/Opt propx={100} optional/>;\nlet opt3 = /*9*/</*10*/Opt wrong />;\nlet opt4 = /*11*/</*12*/Opt propx={100} propString=\"hi\" />;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12");
            }
            finally
            {
                done();
            }
        });
    }

}
