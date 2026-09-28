// ported from: internal/fourslash/tests/organizeImportsUnicode1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class OrganizeImportsUnicode1Case
{
    public static void TestOrganizeImportsUnicode1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("import {\n    Ab,\n    _aB,\n    aB,\n    _Ab,\n} from './foo';\n\nconsole.log(_aB, _Ab, aB, Ab);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "import {\n    Ab,\n    _Ab,\n    _aB,\n    aB,\n} from './foo';\n\nconsole.log(_aB, _Ab, aB, Ab);", LSP.CodeActionKind.SourceOrganizeImportsTs, new LSUP.UserPreferences { OrganizeImportsIgnoreCase = TRI.TSFalse, OrganizeImportsCollation = LSUP.OrganizeImportsCollation.Ordinal });
                f.VerifyOrganizeImports(t, "import {\n    _aB,\n    _Ab,\n    aB,\n    Ab,\n} from './foo';\n\nconsole.log(_aB, _Ab, aB, Ab);", LSP.CodeActionKind.SourceOrganizeImportsTs, new LSUP.UserPreferences { OrganizeImportsIgnoreCase = TRI.TSFalse, OrganizeImportsCollation = LSUP.OrganizeImportsCollation.Unicode });
            }
            finally
            {
                done();
            }
        });
    }

}
