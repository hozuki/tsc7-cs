// ported from: internal/fourslash/tests/tsxSignatureHelp1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class TsxSignatureHelp1Case
{
    public static void TestTsxSignatureHelp1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @jsx: preserve\n//@Filename: file.tsx\nimport React = require('react');\nexport interface ClickableProps {\n    children?: string;\n    className?: string;\n}\nexport interface ButtonProps extends ClickableProps {\n    onClick(event?: React.MouseEvent<HTMLButtonElement>): void;\n}\nfunction _buildMainButton({ onClick, children, className }: ButtonProps): JSX.Element {\n    return(<button className={className} onClick={onClick}>{ children || 'MAIN BUTTON'}</button>);\n}\nexport function MainButton(props: ButtonProps): JSX.Element {\n    return this._buildMainButton(props);\n}\nlet e1 = <MainButton/*1*/ /*2*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "MainButton(props: ButtonProps): JSX.Element", ParameterSpan = "props: ButtonProps" });
                f.GoToMarker(t, "2");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "MainButton(props: ButtonProps): JSX.Element", ParameterSpan = "props: ButtonProps" });
            }
            finally
            {
                done();
            }
        });
    }

}
