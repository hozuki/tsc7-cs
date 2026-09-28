// ported from: internal/fourslash/tests/organizeImportsAttributes_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class OrganizeImportsAttributesCase
{
    public static void TestOrganizeImportsAttributes(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("import { A } from \"./file\";\nimport { type B } from \"./file\";\nimport { C } from \"./file\" with { type: \"a\" };\nimport { A as D } from \"./file\" with { type: \"b\" };\nimport { E } from \"./file\" with { type: \"a\" };\nimport { A as F } from \"./file\" with { type: \"b\" };\n\ntype G = A | B | C | D | E | F;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "import { A, type B } from \"./file\";\nimport { C, E } from \"./file\" with { type: \"a\" };\nimport { A as D, A as F } from \"./file\" with { type: \"b\" };\n\ntype G = A | B | C | D | E | F;", LSP.CodeActionKind.SourceOrganizeImportsTs, null!);
            }
            finally
            {
                done();
            }
        });
    }

}
