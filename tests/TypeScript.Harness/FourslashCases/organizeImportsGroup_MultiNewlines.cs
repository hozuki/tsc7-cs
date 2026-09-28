// ported from: internal/fourslash/tests/organizeImportsGroup_MultiNewlines_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class OrganizeImportsGroup_MultiNewlinesCase
{
    public static void TestOrganizeImportsGroup_MultiNewlines(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("import c from \"C\";\n\n\nimport d from \"D\";\nimport a from \"A\";\nimport b from \"B\";\n\nconsole.log(a, b, c, d)"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "import c from \"C\";\n\n\nimport a from \"A\";\nimport b from \"B\";\nimport d from \"D\";\n\nconsole.log(a, b, c, d)", LSP.CodeActionKind.SourceOrganizeImportsTs, null!);
            }
            finally
            {
                done();
            }
        });
    }

}
