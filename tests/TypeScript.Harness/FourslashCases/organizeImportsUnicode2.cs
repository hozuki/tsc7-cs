// ported from: internal/fourslash/tests/organizeImportsUnicode2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class OrganizeImportsUnicode2Case
{
    public static void TestOrganizeImportsUnicode2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("import {\n    a2,\n    a100,\n    a1,\n} from './foo';\n\nconsole.log(a1, a2, a100);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "import {\n    a1,\n    a100,\n    a2,\n} from './foo';\n\nconsole.log(a1, a2, a100);", LSP.CodeActionKind.SourceOrganizeImportsTs, new LSUP.UserPreferences { OrganizeImportsIgnoreCase = TRI.TSFalse, OrganizeImportsCollation = LSUP.OrganizeImportsCollation.Unicode, OrganizeImportsNumericCollation = TRI.TSFalse });
                f.VerifyOrganizeImports(t, "import {\n    a1,\n    a2,\n    a100,\n} from './foo';\n\nconsole.log(a1, a2, a100);", LSP.CodeActionKind.SourceOrganizeImportsTs, new LSUP.UserPreferences { OrganizeImportsIgnoreCase = TRI.TSFalse, OrganizeImportsCollation = LSUP.OrganizeImportsCollation.Unicode, OrganizeImportsNumericCollation = TRI.TSTrue });
            }
            finally
            {
                done();
            }
        });
    }

}
