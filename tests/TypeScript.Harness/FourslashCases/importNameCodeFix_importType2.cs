// ported from: internal/fourslash/tests/importNameCodeFix_importType2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFix_importType2Case
{
    public static void TestImportNameCodeFix_importType2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @verbatimModuleSyntax: true\n// @module: es2015\n// @Filename: /exports1.ts\nexport default interface SomeType {}\nexport interface OtherType {}\nexport interface OtherOtherType {}\nexport const someValue = 0;\n// @Filename: /a.ts\nimport type SomeType from \"./exports1.js\";\nsomeValue/*a*/\n// @Filename: /b.ts\nimport { someValue } from \"./exports1.js\";\nconst b: SomeType/*b*/ = someValue;\n// @Filename: /c.ts\nimport type SomeType from \"./exports1.js\";\nconst x: OtherType/*c*/\n// @Filename: /d.ts\nimport type { OtherType } from \"./exports1.js\";\nconst x: OtherOtherType/*d*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "a");
                f.VerifyImportFixAtPosition(t, ["import type SomeType from \"./exports1.js\";\nimport { someValue } from \"./exports1.js\";\nsomeValue"], null!);
                f.GoToMarker(t, "b");
                f.VerifyImportFixAtPosition(t, ["import type SomeType from \"./exports1.js\";\nimport { someValue } from \"./exports1.js\";\nconst b: SomeType = someValue;"], null!);
                f.GoToMarker(t, "c");
                f.VerifyImportFixAtPosition(t, ["import type { OtherType } from \"./exports1.js\";\nimport type SomeType from \"./exports1.js\";\nconst x: OtherType"], null!);
                f.GoToMarker(t, "d");
                f.VerifyImportFixAtPosition(t, ["import type { OtherOtherType, OtherType } from \"./exports1.js\";\nconst x: OtherOtherType"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
