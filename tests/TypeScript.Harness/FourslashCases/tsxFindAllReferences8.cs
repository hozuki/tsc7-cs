// ported from: internal/fourslash/tests/tsxFindAllReferences8_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class TsxFindAllReferences8Case
{
    public static void TestTsxFindAllReferences8(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("//@Filename: file.tsx\n// @jsx: preserve\n// @noLib: true\ndeclare namespace JSX {\n    interface Element { }\n    interface IntrinsicElements {\n    }\n    interface ElementAttributesProperty { props; }\n}\ninterface ClickableProps {\n    children?: string;\n    className?: string;\n}\ninterface ButtonProps extends ClickableProps {\n    onClick(event?: React.MouseEvent<HTMLButtonElement>): void;\n}\ninterface LinkProps extends ClickableProps {\n    goTo: string;\n}\n/*1*/declare function /*2*/MainButton(buttonProps: ButtonProps): JSX.Element;\n/*3*/declare function /*4*/MainButton(linkProps: LinkProps): JSX.Element;\n/*5*/declare function /*6*/MainButton(props: ButtonProps | LinkProps): JSX.Element;\nlet opt = /*7*/</*8*/MainButton />;\nlet opt = /*9*/</*10*/MainButton children=\"chidlren\" />;\nlet opt = /*11*/</*12*/MainButton onClick={()=>{}} />;\nlet opt = /*13*/</*14*/MainButton onClick={()=>{}} ignore-prop />;\nlet opt = /*15*/</*16*/MainButton goTo=\"goTo\" />;\nlet opt = /*17*/</*18*/MainButton wrong />;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12", "13", "14", "15", "16", "17", "18");
            }
            finally
            {
                done();
            }
        });
    }

}
