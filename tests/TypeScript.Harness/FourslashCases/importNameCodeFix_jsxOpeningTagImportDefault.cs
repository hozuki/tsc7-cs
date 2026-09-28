// ported from: internal/fourslash/tests/importNameCodeFix_jsxOpeningTagImportDefault_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFix_jsxOpeningTagImportDefaultCase
{
    public static void TestImportNameCodeFix_jsxOpeningTagImportDefault(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: commonjs\n// @jsx: react-jsx\n// @Filename: /component.tsx\nexport default function (props: any) {}\n// @Filename: /index.tsx\nexport function Index() {\n    return <Component/**/ />;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifyImportFixAtPosition(t, ["import Component from \"./component\";\n\nexport function Index() {\n    return <Component />;\n}"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
