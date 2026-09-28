// ported from: internal/fourslash/tests/codeFixPromoteTypeOnlyImportJsxTag_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixPromoteTypeOnlyImportJsxTagCase
{
    public static void TestCodeFixPromoteTypeOnlyImportJsxTag(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: preserve\n// @verbatimModuleSyntax: true\n// @jsx: react\n// @Filename: /react.ts\nconst React: any = {};\nexport default React;\n// @Filename: /bar.tsx\nimport type React from \"./react\";\n\n<Foo/**/ />;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifyImportFixAtPosition(t, ["import React from \"./react\";\n\n<Foo />;"], null!);
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestCodeFixPromoteTypeOnlyImportJsxTagBothTypeOnly(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: preserve\n// @verbatimModuleSyntax: true\n// @jsx: react\n// @Filename: /react.ts\nconst React: any = {};\nexport default React;\n// @Filename: /foo.ts\nexport function Foo() { return null; }\n// @Filename: /bar.tsx\nimport type React from \"./react\";\nimport type { Foo } from \"./foo\";\n\n<Foo/**/ />;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifyImportFixAtPosition(t, ["import type React from \"./react\";\nimport { Foo } from \"./foo\";\n\n<Foo />;", "import React from \"./react\";\nimport type { Foo } from \"./foo\";\n\n<Foo />;"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
