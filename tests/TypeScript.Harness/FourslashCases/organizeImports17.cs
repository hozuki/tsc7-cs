// ported from: internal/fourslash/tests/organizeImports17_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class OrganizeImports17Case
{
    public static void TestOrganizeImports17(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("import { Both } from \"module-specifiers-unsorted\";\nimport { aa, CaseInsensitively, sorted } from \"aardvark\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "import { aa, CaseInsensitively, sorted } from \"aardvark\";\nimport { Both } from \"module-specifiers-unsorted\";\n", LSP.CodeActionKind.SourceSortImportsTs, new LSUP.UserPreferences { OrganizeImportsIgnoreCase = TRI.TSUnknown });
            }
            finally
            {
                done();
            }
        });
    }

}
