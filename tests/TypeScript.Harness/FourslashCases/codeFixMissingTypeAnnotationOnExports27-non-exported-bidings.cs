// ported from: internal/fourslash/tests/codeFixMissingTypeAnnotationOnExports27-non-exported-bidings_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixMissingTypeAnnotationOnExports27_non_exported_bidingsCase
{
    public static void TestCodeFixMissingTypeAnnotationOnExports27_non_exported_bidings(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @isolatedDeclarations: true\n// @declaration: true\nlet p = { x: 1, y: 2}\nconst a = 1, b = 10, { x, y } = p, c = 1;\nexport { x, y }\nexport const d = a + b + c;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFixAll(t, new F.VerifyCodeFixAllOptions { FixID = "fixMissingTypeAnnotationOnExports", NewFileContent = "let p = { x: 1, y: 2}\nconst x: number = p.x;\nconst y: number = p.y;\nconst a = 1, b = 10, c = 1;\nexport { x, y }\nexport const d: number = a + b + c;" });
            }
            finally
            {
                done();
            }
        });
    }

}
