// ported from: internal/fourslash/tests/organizeImportsType6_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class OrganizeImportsType6Case
{
    public static void TestOrganizeImportsType6(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("import { type a, A, b } from \"foo\";\ninterface Use extends A {}\nconsole.log(a, b);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "import { type a, A, b } from \"foo\";\ninterface Use extends A {}\nconsole.log(a, b);", LSP.CodeActionKind.SourceOrganizeImportsTs, new LSUP.UserPreferences { OrganizeImportsTypeOrder = LSUP.OrganizeImportsTypeOrder.Inline });
                f.ReplaceLine(t, 0, "import { type a, A, b } from \"foo1\";");
                f.VerifyOrganizeImports(t, "import { type a, A, b } from \"foo1\";\ninterface Use extends A {}\nconsole.log(a, b);", LSP.CodeActionKind.SourceOrganizeImportsTs, new LSUP.UserPreferences { OrganizeImportsIgnoreCase = TRI.TSUnknown, OrganizeImportsTypeOrder = LSUP.OrganizeImportsTypeOrder.Inline });
                f.ReplaceLine(t, 0, "import { type a, A, b } from \"foo2\";");
                f.VerifyOrganizeImports(t, "import { type a, A, b } from \"foo2\";\ninterface Use extends A {}\nconsole.log(a, b);", LSP.CodeActionKind.SourceOrganizeImportsTs, new LSUP.UserPreferences { OrganizeImportsIgnoreCase = TRI.TSTrue, OrganizeImportsTypeOrder = LSUP.OrganizeImportsTypeOrder.Inline });
                f.ReplaceLine(t, 0, "import { type a, A, b } from \"foo3\";");
                f.VerifyOrganizeImports(t, "import { A, type a, b } from \"foo3\";\ninterface Use extends A {}\nconsole.log(a, b);", LSP.CodeActionKind.SourceOrganizeImportsTs, new LSUP.UserPreferences { OrganizeImportsIgnoreCase = TRI.TSFalse, OrganizeImportsTypeOrder = LSUP.OrganizeImportsTypeOrder.Inline });
            }
            finally
            {
                done();
            }
        });
    }

}
