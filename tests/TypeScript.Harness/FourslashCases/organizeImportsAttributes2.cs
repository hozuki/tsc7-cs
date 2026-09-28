// ported from: internal/fourslash/tests/organizeImportsAttributes2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class OrganizeImportsAttributes2Case
{
    public static void TestOrganizeImportsAttributes2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("import { A } from \"./a\";\nimport { C } from \"./a\" with { type: \"a\" };\nimport { Z } from \"./z\";\nimport { A as D } from \"./a\" with { type: \"b\" };\nimport { E } from \"./a\" with { type: \"a\" };\nimport { F } from \"./a\" with { type: \"a\" };\nimport { B } from \"./a\";\n\nexport type G = A | B | C | D | E | F | Z;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "import { A, B } from \"./a\";\nimport { C, E, F } from \"./a\" with { type: \"a\" };\nimport { A as D } from \"./a\" with { type: \"b\" };\nimport { Z } from \"./z\";\n\nexport type G = A | B | C | D | E | F | Z;", LSP.CodeActionKind.SourceOrganizeImportsTs, null!);
            }
            finally
            {
                done();
            }
        });
    }

}
