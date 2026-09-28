// ported from: internal/fourslash/tests/tsxCompletionInFunctionExpressionOfChildrenCallback_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class TsxCompletionInFunctionExpressionOfChildrenCallbackCase
{
    public static void TestTsxCompletionInFunctionExpressionOfChildrenCallback(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("//@module: commonjs\n//@jsx: preserve\n// @Filename: 1.tsx\ndeclare namespace JSX {\n    interface Element { }\n    interface IntrinsicElements {\n    }\n    interface ElementAttributesProperty { props; }\n}\ninterface IUser {\n    Name: string;\n}\ninterface IFetchUserProps {\n    children: (user: IUser) => any;\n}\nfunction FetchUser(props: IFetchUserProps) { return undefined; }\nfunction UserName() {\n    return (\n        <FetchUser>\n            { user => (\n                <h1>{ user./**/ }</h1>\n            )}\n        </FetchUser>\n    );\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", null!);
            }
            finally
            {
                done();
            }
        });
    }

}
