// ported from: internal/fourslash/tests/importNameCodeFixExistingImport2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFixExistingImport2Case
{
    public static void TestImportNameCodeFixExistingImport2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("import * as ns from \"./module\";\n// Comment\nf1/*0*/();\n// @Filename: module.ts\n export function f1() {}\n export var v1 = 5;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixAtPosition(t, ["import * as ns from \"./module\";\n// Comment\nns.f1();", "import * as ns from \"./module\";\nimport { f1 } from \"./module\";\n// Comment\nf1();"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
