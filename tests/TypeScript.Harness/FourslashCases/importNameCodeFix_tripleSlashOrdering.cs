// ported from: internal/fourslash/tests/importNameCodeFix_tripleSlashOrdering_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFix_tripleSlashOrderingCase
{
    public static void TestImportNameCodeFix_tripleSlashOrdering(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /tsconfig.json\n{\n    \"compilerOptions\": {\n        \"skipDefaultLibCheck\": false\n    }\n}\n// @Filename: /a.ts\nexport const x = 0;\n// @Filename: /b.ts\n// some comment\n\n/// <reference lib=\"es2017.string\" />\n\nconst y = x + 1;\n// @Filename: /c.ts\n// some comment\n\n/// <reference path=\"jquery-1.8.3.js\" />\n\nconst y = x + 1;\n// @Filename: /d.ts\n// some comment\n\n/// <reference types=\"node\" />\n\nconst y = x + 1;\n// @Filename: /f.ts\n// some comment\n\n/// <amd-module name=\"NamedModule\" />\n\nconst y = x + 1;\n// @Filename: /g.ts\n// some comment\n\n/// <amd-dependency path=\"legacy/moduleA\" name=\"moduleA\" />\n\nconst y = x + 1;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/b.ts");
                f.VerifyImportFixAtPosition(t, ["// some comment\n\n/// <reference lib=\"es2017.string\" />\n\nimport { x } from \"./a\";\n\nconst y = x + 1;"], null!);
                f.GoToFile(t, "/c.ts");
                f.VerifyImportFixAtPosition(t, ["// some comment\n\n/// <reference path=\"jquery-1.8.3.js\" />\n\nimport { x } from \"./a\";\n\nconst y = x + 1;"], null!);
                f.GoToFile(t, "/d.ts");
                f.VerifyImportFixAtPosition(t, ["// some comment\n\n/// <reference types=\"node\" />\n\nimport { x } from \"./a\";\n\nconst y = x + 1;"], null!);
                f.GoToFile(t, "/f.ts");
                f.VerifyImportFixAtPosition(t, ["// some comment\n\n/// <amd-module name=\"NamedModule\" />\n\nimport { x } from \"./a\";\n\nconst y = x + 1;"], null!);
                f.GoToFile(t, "/g.ts");
                f.VerifyImportFixAtPosition(t, ["// some comment\n\n/// <amd-dependency path=\"legacy/moduleA\" name=\"moduleA\" />\n\nimport { x } from \"./a\";\n\nconst y = x + 1;"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
