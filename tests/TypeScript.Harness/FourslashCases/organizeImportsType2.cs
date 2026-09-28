// ported from: internal/fourslash/tests/organizeImportsType2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using LSUP = TypeScript.LsUtil;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class OrganizeImportsType2Case
{
    public static void TestOrganizeImportsType2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowSyntheticDefaultImports: true\n// @moduleResolution: bundler\n// @noUnusedLocals: true\n// @target: es2018\ntype A = string;\ntype B = string;\nconst C = \"hello\";\nexport { A, type B, C };"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "type A = string;\ntype B = string;\nconst C = \"hello\";\nexport { A, C, type B };\n", LSP.CodeActionKind.SourceOrganizeImportsTs, null!);
                f.VerifyOrganizeImports(t, "type A = string;\ntype B = string;\nconst C = \"hello\";\nexport { A, type B, C };\n", LSP.CodeActionKind.SourceOrganizeImportsTs, new LSUP.UserPreferences { OrganizeImportsTypeOrder = LSUP.OrganizeImportsTypeOrder.Inline });
                f.VerifyOrganizeImports(t, "type A = string;\ntype B = string;\nconst C = \"hello\";\nexport { type B, A, C };\n", LSP.CodeActionKind.SourceOrganizeImportsTs, new LSUP.UserPreferences { OrganizeImportsTypeOrder = LSUP.OrganizeImportsTypeOrder.First });
                f.VerifyOrganizeImports(t, "type A = string;\ntype B = string;\nconst C = \"hello\";\nexport { A, C, type B };\n", LSP.CodeActionKind.SourceOrganizeImportsTs, new LSUP.UserPreferences { OrganizeImportsTypeOrder = LSUP.OrganizeImportsTypeOrder.Last });
            }
            finally
            {
                done();
            }
        });
    }

}
