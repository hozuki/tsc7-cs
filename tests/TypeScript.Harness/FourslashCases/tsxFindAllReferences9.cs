// ported from: internal/fourslash/tests/tsxFindAllReferences9_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class TsxFindAllReferences9Case
{
    public static void TestTsxFindAllReferences9(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("//@Filename: file.tsx\n// @jsx: preserve\n// @noLib: true\ndeclare namespace JSX {\n    interface Element { }\n    interface IntrinsicElements {\n    }\n    interface ElementAttributesProperty { props; }\n}\ninterface ClickableProps {\n    children?: string;\n    className?: string;\n}\ninterface ButtonProps extends ClickableProps {\n    onClick(event?: React.MouseEvent<HTMLButtonElement>): void;\n}\ninterface LinkProps extends ClickableProps {\n    /*1*/goTo: string;\n}\ndeclare function MainButton(buttonProps: ButtonProps): JSX.Element;\ndeclare function MainButton(linkProps: LinkProps): JSX.Element;\ndeclare function MainButton(props: ButtonProps | LinkProps): JSX.Element;\nlet opt = <MainButton />;\nlet opt = <MainButton children=\"chidlren\" />;\nlet opt = <MainButton onClick={()=>{}} />;\nlet opt = <MainButton onClick={()=>{}} ignore-prop />;\nlet opt = <MainButton goTo=\"goTo\" />;\nlet opt = <MainButton goTo />;\nlet opt = <MainButton wrong />;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1");
            }
            finally
            {
                done();
            }
        });
    }

}
