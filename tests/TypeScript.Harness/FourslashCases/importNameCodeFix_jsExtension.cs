// ported from: internal/fourslash/tests/importNameCodeFix_jsExtension_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFix_jsExtensionCase
{
    public static void TestImportNameCodeFix_jsExtension(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @noLib: true\n// @jsx: preserve\n// @Filename: /a.ts\nexport function a() {}\n// @Filename: /b.ts\nexport function b() {}\n// @Filename: /c.tsx\nexport function c() {}\n// @Filename: /c.ts\nimport * as g from \"global\"; // Global imports skipped\nimport { a } from \"./a.js\";\nimport { a as a2 } from \"./a\"; // Ignored, only the first relative import is considered\nb; c;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/c.ts");
                f.VerifyCodeFixAll(t, new F.VerifyCodeFixAllOptions { FixID = "fixMissingImport", NewFileContent = "import * as g from \"global\"; // Global imports skipped\nimport { a } from \"./a.js\";\nimport { a as a2 } from \"./a\"; // Ignored, only the first relative import is considered\nimport { b } from \"./b.js\";\nimport { c } from \"./c.jsx\";\nb; c;" });
            }
            finally
            {
                done();
            }
        });
    }

}
