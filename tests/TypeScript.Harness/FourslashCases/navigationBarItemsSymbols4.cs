// ported from: internal/fourslash/tests/navigationBarItemsSymbols4_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class NavigationBarItemsSymbols4Case
{
    public static void TestNavigationBarItemsSymbols4(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @checkJs: true\n// @allowJs: true\n// @target: es6\n// @Filename: file.js\nconst _sym = Symbol(\"_sym\");\nclass MyClass {\n    constructor() {\n        // Dynamic assignment properties can't show up in navigation,\n        // as they're not syntactic members\n        // Additonally, late bound members are always filtered out, besides\n        this[_sym] = \"ok\";\n    }\n\n    method() {\n        this[_sym] = \"yep\";\n        const x = this[_sym];\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineDocumentSymbol(t);
            }
            finally
            {
                done();
            }
        });
    }

}
