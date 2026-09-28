// ported from: internal/fourslash/tests/organizeImports16_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class OrganizeImports16Case
{
    public static void TestOrganizeImports16(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("import { a, A, b } from \"foo\";\ninterface Use extends A {}\nconsole.log(a, b);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "import { a, A, b } from \"foo\";\ninterface Use extends A {}\nconsole.log(a, b);", LSP.CodeActionKind.SourceOrganizeImportsTs, null!);
                f.ReplaceLine(t, 0, "import { a, A, b } from \"foo1\";");
                f.VerifyOrganizeImports(t, "import { a, A, b } from \"foo1\";\ninterface Use extends A {}\nconsole.log(a, b);", LSP.CodeActionKind.SourceOrganizeImportsTs, new LSUP.UserPreferences { OrganizeImportsIgnoreCase = TRI.TSUnknown });
                f.ReplaceLine(t, 0, "import { a, A, b } from \"foo2\";");
                f.VerifyOrganizeImports(t, "import { a, A, b } from \"foo2\";\ninterface Use extends A {}\nconsole.log(a, b);", LSP.CodeActionKind.SourceOrganizeImportsTs, new LSUP.UserPreferences { OrganizeImportsIgnoreCase = TRI.TSTrue });
                f.ReplaceLine(t, 0, "import { a, A, b } from \"foo3\";");
                f.VerifyOrganizeImports(t, "import { A, a, b } from \"foo3\";\ninterface Use extends A {}\nconsole.log(a, b);", LSP.CodeActionKind.SourceOrganizeImportsTs, new LSUP.UserPreferences { OrganizeImportsIgnoreCase = TRI.TSFalse });
            }
            finally
            {
                done();
            }
        });
    }

}
