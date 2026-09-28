// ported from: internal/fourslash/tests/autoImportSortCaseSensitivity1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportSortCaseSensitivity1Case
{
    public static void TestAutoImportSortCaseSensitivity1(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /exports1.ts\nexport const a = 0;\nexport const A = 1;\nexport const b = 2;\nexport const B = 3;\nexport const c = 4;\nexport const C = 5;\n// @Filename: /exports2.ts\nexport const d = 0;\nexport const D = 1;\nexport const e = 2;\nexport const E = 3;\n// @Filename: /index0.ts\nimport { A, B, C } from \"./exports1\";\na/*0*/\n// @Filename: /index1.ts\nimport { A, a, B, b } from \"./exports1\";\nimport { E } from \"./exports2\";\nd/*1*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "0");
                f.VerifyImportFixAtPosition(t, ["import { a, A, B, C } from \"./exports1\";\na"], null!);
                f.VerifyImportFixAtPosition(t, ["import { a, A, B, C } from \"./exports1\";\na"], null!);
                f.GoToMarker(t, "1");
                f.VerifyImportFixAtPosition(t, ["import { A, a, B, b } from \"./exports1\";\nimport { d, E } from \"./exports2\";\nd"], null!);
                f.VerifyImportFixAtPosition(t, ["import { A, a, B, b } from \"./exports1\";\nimport { E, d } from \"./exports2\";\nd"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
