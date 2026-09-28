// ported from: internal/fourslash/tests/importNameCodeFix_importType3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFix_importType3Case
{
    public static void TestImportNameCodeFix_importType3(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @verbatimModuleSyntax: true\n// @module: es2015\n// @Filename: /exports.ts\nclass SomeClass {}\nexport type { SomeClass };\n// @Filename: /a.ts\nimport {} from \"./exports.js\";\nfunction takeSomeClass(c: SomeClass/**/)"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifyImportFixAtPosition(t, ["import { type SomeClass } from \"./exports.js\";\nfunction takeSomeClass(c: SomeClass)"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
