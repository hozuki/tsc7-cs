// ported from: internal/fourslash/tests/tsxFindAllReferencesUnionElementType2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class TsxFindAllReferencesUnionElementType2Case
{
    public static void TestTsxFindAllReferencesUnionElementType2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("//@Filename: file.tsx\n// @jsx: preserve\n// @noLib: true\nclass RC1 extends React.Component<{}, {}> {\n    render() {\n        return null;\n    }\n}\nclass RC2 extends React.Component<{}, {}> {\n    render() {\n        return null;\n    }\n    private method() { }\n}\n/*1*/var /*2*/RCComp = RC1 || RC2;\n/*3*/</*4*/RCComp />"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1", "2", "3", "4");
            }
            finally
            {
                done();
            }
        });
    }

}
