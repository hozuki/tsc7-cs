// ported from: internal/fourslash/tests/organizeImportsAttributes4_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class OrganizeImportsAttributes4Case
{
    public static void TestOrganizeImportsAttributes4(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("import { A } from \"./a\" with { foo: \"foo\", bar: \"bar\" };\nimport { B } from \"./a\" with { bar: \"bar\", foo: \"foo\" };\nimport { D } from \"./a\" with { bar: \"foo\", foo: \"bar\" };\nimport { E } from \"./a\" with { foo: 'bar', bar: \"foo\" };\nimport { C } from \"./a\" with { foo: \"bar\", bar: \"foo\" };\nimport { F } from \"./a\" with { foo: \"42\" };\nimport { Y } from \"./a\" with { foo: 42 };\nimport { Z } from \"./a\" with { foo: \"42\" };\n\nexport type G = A | B | C | D | E | F | Y | Z;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "import { A, B } from \"./a\" with { foo: \"foo\", bar: \"bar\" };\nimport { C, D, E } from \"./a\" with { bar: \"foo\", foo: \"bar\" };\nimport { F, Z } from \"./a\" with { foo: \"42\" };\nimport { Y } from \"./a\" with { foo: 42 };\n\nexport type G = A | B | C | D | E | F | Y | Z;", LSP.CodeActionKind.SourceOrganizeImportsTs, null!);
            }
            finally
            {
                done();
            }
        });
    }

}
