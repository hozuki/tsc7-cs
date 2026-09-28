// ported from: internal/fourslash/tests/tsxGoToDefinitionStatelessFunction2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class TsxGoToDefinitionStatelessFunction2Case
{
    public static void TestTsxGoToDefinitionStatelessFunction2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("//@Filename: file.tsx\n// @jsx: preserve\n// @noLib: true\ndeclare namespace JSX {\n    interface Element { }\n    interface IntrinsicElements {\n    }\n    interface ElementAttributesProperty { props; }\n}\ninterface ClickableProps {\n    children?: string;\n    className?: string;\n}\ninterface ButtonProps extends ClickableProps {\n    onClick(event?: React.MouseEvent<HTMLButtonElement>): void;\n}\ninterface LinkProps extends ClickableProps {\n    goTo: string;\n}\ndeclare function /*firstSource*/MainButton(buttonProps: ButtonProps): JSX.Element;\ndeclare function /*secondSource*/MainButton(linkProps: LinkProps): JSX.Element;\ndeclare function /*thirdSource*/MainButton(props: ButtonProps | LinkProps): JSX.Element;\nlet opt = <[|Main/*firstTarget*/Button|] />;\nlet opt = <[|Main/*secondTarget*/Button|] children=\"chidlren\" />;\nlet opt = <[|Main/*thirdTarget*/Button|] onClick={()=>{}} />;\nlet opt = <[|Main/*fourthTarget*/Button|] onClick={()=>{}} ignore-prop />;\nlet opt = <[|Main/*fifthTarget*/Button|] goTo=\"goTo\" />;\nlet opt = <[|Main/*sixthTarget*/Button|] wrong />;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "firstTarget", "secondTarget", "thirdTarget", "fourthTarget", "fifthTarget", "sixthTarget");
            }
            finally
            {
                done();
            }
        });
    }

}
