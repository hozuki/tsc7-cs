// ported from: internal/fourslash/tests/codeFixPromoteTypeOnlyOrderingCrash_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixPromoteTypeOnlyOrderingCrashCase
{
    public static void TestCodeFixPromoteTypeOnlyOrderingCrash(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: node18\n// @verbatimModuleSyntax: true\n// @Filename: /bar.ts\nexport interface AAA {}\nexport class BBB {}\n// @Filename: /foo.ts\nimport type {\n    AAA,\n    BBB,\n} from \"./bar\";\n\nlet x: AAA = new BBB()"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/foo.ts");
                f.VerifyImportFixAtPosition(t, ["import {\n    BBB,\n    type AAA,\n} from \"./bar\";\n\nlet x: AAA = new BBB()"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
