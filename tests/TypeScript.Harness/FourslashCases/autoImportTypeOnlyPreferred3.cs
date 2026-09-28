// ported from: internal/fourslash/tests/autoImportTypeOnlyPreferred3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportTypeOnlyPreferred3Case
{
    public static void TestAutoImportTypeOnlyPreferred3(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: esnext\n// @moduleResolution: bundler\n// @Filename: /a.ts\nexport class A {}\nexport class B {}\n// @Filename: /b.ts\nlet x: A/*b*/;\n// @Filename: /c.ts\nimport { A } from \"./a\";\nnew A();\nlet x: B/*c*/;\n// @Filename: /d.ts\nnew A();\nlet x: B;\n// @Filename: /ns.ts\nexport * as default from \"./a\";\n// @Filename: /e.ts\nlet x: /*e*/ns.A;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "b");
                f.VerifyImportFixAtPosition(t, ["import type { A } from \"./a\";\n\nlet x: A;"], new LSUP.UserPreferences { PreferTypeOnlyAutoImports = TRI.TSTrue });
                f.GoToMarker(t, "c");
                f.VerifyImportFixAtPosition(t, ["import { A, type B } from \"./a\";\nnew A();\nlet x: B;"], new LSUP.UserPreferences { PreferTypeOnlyAutoImports = TRI.TSTrue });
                f.GoToFile(t, "/d.ts");
                f.VerifyCodeFixAll(t, new F.VerifyCodeFixAllOptions { FixID = "fixMissingImport", NewFileContent = "import { A, type B } from \"./a\";\n\nnew A();\nlet x: B;" });
                f.GoToMarker(t, "e");
                f.VerifyImportFixAtPosition(t, ["import type ns from \"./ns\";\n\nlet x: ns.A;"], new LSUP.UserPreferences { PreferTypeOnlyAutoImports = TRI.TSTrue });
            }
            finally
            {
                done();
            }
        });
    }

}
