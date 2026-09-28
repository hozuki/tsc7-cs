// ported from: internal/fourslash/tests/organizeImportsPathsUnicode3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class OrganizeImportsPathsUnicode3Case
{
    public static void TestOrganizeImportsPathsUnicode3(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("import * as B from \"./B\";\nimport * as \u00C0 from \"./\u00C0\";\nimport * as A from \"./A\";\n\nconsole.log(A, \u00C0, B);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "import * as A from \"./A\";\nimport * as \u00c0 from \"./\u00c0\";\nimport * as B from \"./B\";\n\nconsole.log(A, \u00c0, B);", LSP.CodeActionKind.SourceOrganizeImportsTs, new LSUP.UserPreferences { OrganizeImportsIgnoreCase = TRI.TSFalse, OrganizeImportsCollation = LSUP.OrganizeImportsCollation.Unicode, OrganizeImportsAccentCollation = TRI.TSFalse });
                f.VerifyOrganizeImports(t, "import * as A from \"./A\";\nimport * as \u00c0 from \"./\u00c0\";\nimport * as B from \"./B\";\n\nconsole.log(A, \u00c0, B);", LSP.CodeActionKind.SourceOrganizeImportsTs, new LSUP.UserPreferences { OrganizeImportsIgnoreCase = TRI.TSFalse, OrganizeImportsCollation = LSUP.OrganizeImportsCollation.Unicode, OrganizeImportsAccentCollation = TRI.TSTrue });
            }
            finally
            {
                done();
            }
        });
    }

}
