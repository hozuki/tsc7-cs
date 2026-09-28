// ported from: internal/fourslash/tests/renameNamedImportUseAliasesForRenames_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameNamedImportUseAliasesForRenamesCase
{
    public static void TestRenameNamedImportUseAliasesForRenames(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /a.ts\nimport { /*import*/MyTypeA } from \"./b\";\nconst type: MyTypeA = { foo: \"bar\" };\n// @Filename: /b.ts\nexport interface MyTypeA {\n    foo: string;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineRename(t, new LSUP.UserPreferences { UseAliasesForRename = TRI.TSFalse }, "import");
                f.VerifyBaselineRename(t, new LSUP.UserPreferences { UseAliasesForRename = TRI.TSTrue }, "import");
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestRenameNamedImportDefaultInNodeModules(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /index.ts\nimport { /*fooImport*/[|Foo|] } from \"foo\";\ndeclare const f: Foo;\n// @Filename: /tsconfig.json\n{}\n// @Filename: /node_modules/foo/package.json\n{ \"types\": \"index.d.ts\" }\n// @Filename: /node_modules/foo/index.d.ts\nexport interface Foo {\n    bar: string;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineRename(t, null!, "fooImport");
                f.VerifyBaselineRename(t, new LSUP.UserPreferences { UseAliasesForRename = TRI.TSTrue }, "fooImport");
                f.GoToMarker(t, "fooImport");
                f.VerifyRenameFailed(t, new LSUP.UserPreferences { UseAliasesForRename = TRI.TSFalse });
            }
            finally
            {
                done();
            }
        });
    }

}
