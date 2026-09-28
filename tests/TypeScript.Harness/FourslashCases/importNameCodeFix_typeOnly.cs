// ported from: internal/fourslash/tests/importNameCodeFix_typeOnly_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFix_typeOnlyCase
{
    public static void TestImportNameCodeFix_typeOnly(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: esnext\n// @verbatimModuleSyntax: true\n// @Filename: types.ts\nexport class A {}\n// @Filename: index.ts\nconst a: /**/A"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifyImportFixAtPosition(t, ["import type { A } from \"./types\";\n\nconst a: A"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
