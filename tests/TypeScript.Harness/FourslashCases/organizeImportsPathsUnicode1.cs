// ported from: internal/fourslash/tests/organizeImportsPathsUnicode1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class OrganizeImportsPathsUnicode1Case
{
    public static void TestOrganizeImportsPathsUnicode1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("import * as Ab from \"./Ab\";\nimport * as _aB from \"./_aB\";\nimport * as aB from \"./aB\";\nimport * as _Ab from \"./_Ab\";\n\nconsole.log(_aB, _Ab, aB, Ab);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "import * as Ab from \"./Ab\";\nimport * as _Ab from \"./_Ab\";\nimport * as _aB from \"./_aB\";\nimport * as aB from \"./aB\";\n\nconsole.log(_aB, _Ab, aB, Ab);", LSP.CodeActionKind.SourceOrganizeImportsTs, new LSUP.UserPreferences { OrganizeImportsIgnoreCase = TRI.TSFalse, OrganizeImportsCollation = LSUP.OrganizeImportsCollation.Ordinal });
                f.VerifyOrganizeImports(t, "import * as _aB from \"./_aB\";\nimport * as _Ab from \"./_Ab\";\nimport * as aB from \"./aB\";\nimport * as Ab from \"./Ab\";\n\nconsole.log(_aB, _Ab, aB, Ab);", LSP.CodeActionKind.SourceOrganizeImportsTs, new LSUP.UserPreferences { OrganizeImportsIgnoreCase = TRI.TSFalse, OrganizeImportsCollation = LSUP.OrganizeImportsCollation.Unicode });
            }
            finally
            {
                done();
            }
        });
    }

}
