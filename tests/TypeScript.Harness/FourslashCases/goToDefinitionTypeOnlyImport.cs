// ported from: internal/fourslash/tests/goToDefinitionTypeOnlyImport_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionTypeOnlyImportCase
{
    public static void TestGoToDefinitionTypeOnlyImport(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /a.ts\nenum /*1*/SyntaxKind { SourceFile }\nexport type { SyntaxKind }\n// @Filename: /b.ts\n export type { SyntaxKind } from './a';\n// @Filename: /c.ts\nimport type { SyntaxKind } from './b';\nlet kind: [|/*2*/SyntaxKind|];"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "2");
            }
            finally
            {
                done();
            }
        });
    }

}
