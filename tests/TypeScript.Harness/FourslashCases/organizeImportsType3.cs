// ported from: internal/fourslash/tests/organizeImportsType3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class OrganizeImportsType3Case
{
    public static void TestOrganizeImportsType3(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("import {\n    d, \n    type d as D,\n    type c,\n    c as C,\n    b,\n    b as B,\n    type A,\n    a\n} from './foo';\nconsole.log(A, a, B, b, c, C, d, D);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "import {\n    type A,\n    b as B,\n    c as C,\n    type d as D,\n    a,\n    b,\n    type c,\n    d\n} from './foo';\nconsole.log(A, a, B, b, c, C, d, D);", LSP.CodeActionKind.SourceOrganizeImportsTs, new LSUP.UserPreferences { OrganizeImportsIgnoreCase = TRI.TSFalse, OrganizeImportsTypeOrder = LSUP.OrganizeImportsTypeOrder.Inline });
            }
            finally
            {
                done();
            }
        });
    }

}
