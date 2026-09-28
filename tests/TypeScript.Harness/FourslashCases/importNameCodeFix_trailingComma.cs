// ported from: internal/fourslash/tests/importNameCodeFix_trailingComma_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFix_trailingCommaCase
{
    public static void TestImportNameCodeFix_trailingComma(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: index.ts\nimport {\n  T2,\n  T1,\n} from \"./types\";\n\nconst x: T3/**/\n// @Filename: types.ts\nexport type T1 = 0;\nexport type T2 = 0;\nexport type T3 = 0;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixAtPosition(t, ["import {\n  T2,\n  T1,\n  T3,\n} from \"./types\";\n\nconst x: T3"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
