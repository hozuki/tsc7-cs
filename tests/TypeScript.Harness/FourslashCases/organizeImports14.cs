// ported from: internal/fourslash/tests/organizeImports14_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class OrganizeImports14Case
{
    public static void TestOrganizeImports14(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @filename: /a.ts\nexport const foo = 1;\n// @filename: /b.ts\n/**\n * Module doc comment\n *\n * @module\n */\n\n// comment 1\n\n// comment 2\n\nimport { foo } from \"./a\";\nimport { foo } from \"./a\";\nimport { foo } from \"./a\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/b.ts");
                f.VerifyOrganizeImports(t, "/**\n * Module doc comment\n *\n * @module\n */\n\n// comment 1\n\n// comment 2\n\n", LSP.CodeActionKind.SourceOrganizeImportsTs, null!);
            }
            finally
            {
                done();
            }
        });
    }

}
