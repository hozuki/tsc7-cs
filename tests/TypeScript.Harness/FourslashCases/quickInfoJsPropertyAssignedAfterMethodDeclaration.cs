// ported from: internal/fourslash/tests/quickInfoJsPropertyAssignedAfterMethodDeclaration_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoJsPropertyAssignedAfterMethodDeclarationCase
{
    public static void TestQuickInfoJsPropertyAssignedAfterMethodDeclaration(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @noLib: true\n// @allowJs: true\n// @noImplicitThis: true\n// @Filename: /a.js\nconst o = {\n    test/*1*/() {\n        this./*2*/test = 0;\n    }\n};"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(method) test(): void", "");
                f.VerifyQuickInfoAt(t, "2", "(method) test(): void", "");
            }
            finally
            {
                done();
            }
        });
    }

}
