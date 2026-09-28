// ported from: internal/fourslash/tests/importNameCodeFix_all_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFix_allCase
{
    public static void TestImportNameCodeFix_all(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: commonjs\n// @esModuleInterop: false\n// @allowSyntheticDefaultImports: false\n// @Filename: /a.ts\nexport default function ad() {}\nexport const a0 = 0;\n// @Filename: /b.ts\nexport default function bd() {}\nexport const b0 = 0;\n// @Filename: /c.ts\nexport default function cd() {}\nexport const c0 = 0;\n// @Filename: /d.ts\nexport default function dd() {}\nexport const d0 = 0;\nexport const d1 = 1;\n// @Filename: /e.d.ts\ndeclare function e(): void;\nexport = e;\n// @Filename: /disposable.d.ts\nexport declare class Disposable { }\n// @Filename: /disposable_global.d.ts\ninterface Disposable { }\n// @Filename: /user.ts\nimport * as b from \"./b\";\nimport { } from \"./c\";\nimport dd from \"./d\";\n\nad; ad; a0; a0;\nbd; bd; b0; b0;\ncd; cd; c0; c0;\ndd; dd; d0; d0; d1; d1;\ne; e;\nclass X extends Disposable { }"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/user.ts");
                f.VerifyCodeFixAll(t, new F.VerifyCodeFixAllOptions { FixID = "fixMissingImport", NewFileContent = "import ad, { a0 } from \"./a\";\nimport bd, * as b from \"./b\";\nimport cd, { c0 } from \"./c\";\nimport dd, { d0, d1 } from \"./d\";\nimport { Disposable } from \"./disposable\";\nimport e = require(\"./e\");\n\nad; ad; a0; a0;\nbd; bd; b.b0; b.b0;\ncd; cd; c0; c0;\ndd; dd; d0; d0; d1; d1;\ne; e;\nclass X extends Disposable { }" });
            }
            finally
            {
                done();
            }
        });
    }

}
