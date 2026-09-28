// ported from: internal/fourslash/tests/importNameCodeFix_all_promoteType_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFix_all_promoteTypeCase
{
    public static void TestImportNameCodeFix_all_promoteType(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /a.ts\nexport class A {}\nexport class B {}\nexport class C {}\nexport class D {}\nexport class E {}\nexport class F {}\nexport class G {}\n// @Filename: /b.ts\nimport type { A, C, D, E, G } from './a';\ntype Z = B | A;\nnew F;\n// @Filename: /c.ts\nimport type { A, C, D, E, G } from './a';\ntype Z = B | A;\ntype Y = F;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/b.ts");
                f.VerifyCodeFixAll(t, new F.VerifyCodeFixAllOptions { FixID = "fixMissingImport", NewFileContent = "import { B, F, type A, type C, type D, type E, type G } from './a';\ntype Z = B | A;\nnew F;" });
                f.GoToFile(t, "/c.ts");
                f.VerifyCodeFixAll(t, new F.VerifyCodeFixAllOptions { FixID = "fixMissingImport", NewFileContent = "import type { A, B, C, D, E, F, G } from './a';\ntype Z = B | A;\ntype Y = F;" });
            }
            finally
            {
                done();
            }
        });
    }

}
