// ported from: internal/fourslash/tests/navigationBarItemsItemsModuleVariables_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class NavigationBarItemsItemsModuleVariablesCase
{
    public static void TestNavigationBarItemsItemsModuleVariables(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: navigationItemsModuleVariables_0.ts\n /*file1*/\nnamespace Module1 {\n    export var x = 0;\n}\n// @Filename: navigationItemsModuleVariables_1.ts\n /*file2*/\nnamespace Module1.SubModule {\n    export var y = 0;\n}\n// @Filename: navigationItemsModuleVariables_2.ts\n /*file3*/\nnamespace Module1 {\n    export var z = 0;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "file1");
                f.VerifyBaselineDocumentSymbol(t);
                f.GoToMarker(t, "file2");
                f.VerifyBaselineDocumentSymbol(t);
            }
            finally
            {
                done();
            }
        });
    }

}
