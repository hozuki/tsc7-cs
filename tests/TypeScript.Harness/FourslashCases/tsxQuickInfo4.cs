// ported from: internal/fourslash/tests/tsxQuickInfo4_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class TsxQuickInfo4Case
{
    public static void TestTsxQuickInfo4(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("//@Filename: file.tsx\n// @jsx: preserve\n// @noLib: true\nexport interface ClickableProps {\n    children?: string;\n    className?: string;\n}\nexport interface ButtonProps extends ClickableProps {\n    onClick(event?: React.MouseEvent<HTMLButtonElement>): void;\n}\nexport interface LinkProps extends ClickableProps {\n    to: string;\n}\nexport function MainButton(buttonProps: ButtonProps): JSX.Element;\nexport function MainButton(linkProps: LinkProps): JSX.Element;\nexport function MainButton(props: ButtonProps | LinkProps): JSX.Element {\n    const linkProps = props as LinkProps;\n    if(linkProps.to) {\n        return this._buildMainLink(props);\n    }\n    return this._buildMainButton(props);\n}\nfunction _buildMainButton({ onClick, children, className }: ButtonProps): JSX.Element {\n    return(<button className={className} onClick={onClick}>{ children || 'MAIN BUTTON'}</button>);\n}\ndeclare function buildMainLink({ to, children, className }: LinkProps): JSX.Element;\nfunction buildSomeElement1(): JSX.Element {\n    return (\n        <MainB/*1*/utton t/*2*/o='/some/path'>GO</MainButton>\n    );\n}\nfunction buildSomeElement2(): JSX.Element {\n    return (\n        <MainB/*3*/utton onC/*4*/lick={()=>{}}>GO</MainButton>;\n    );\n}\nlet componenet = <MainButton onClick={()=>{}} ext/*5*/ra-prop>GO</MainButton>;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "function MainButton(linkProps: LinkProps): JSX.Element (+1 overload)", "");
                f.VerifyQuickInfoAt(t, "2", "(property) LinkProps.to: string", "");
                f.VerifyQuickInfoAt(t, "3", "function MainButton(buttonProps: ButtonProps): JSX.Element (+1 overload)", "");
                f.VerifyQuickInfoAt(t, "4", "(method) ButtonProps.onClick(event?: React.MouseEvent<HTMLButtonElement>): void", "");
                f.VerifyQuickInfoAt(t, "5", "(property) extra-prop: true", "");
            }
            finally
            {
                done();
            }
        });
    }

}
