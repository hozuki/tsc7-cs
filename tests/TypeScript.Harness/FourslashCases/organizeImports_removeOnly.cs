// ported from: internal/fourslash/tests/organizeImports_removeOnly_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class OrganizeImports_removeOnlyCase
{
    public static void TestOrganizeImports_removeOnly(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("import { c, b, a } from \"foo\";\nimport d, { e } from \"bar\";\nimport * as f from \"baz\";\nimport { g } from \"foo\";\n\nexport { g, e, b, c };"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "import { c, b } from \"foo\";\nimport { e } from \"bar\";\nimport { g } from \"foo\";\n\nexport { g, e, b, c };", LSP.CodeActionKind.SourceRemoveUnusedImportsTs, null!);
            }
            finally
            {
                done();
            }
        });
    }

}
