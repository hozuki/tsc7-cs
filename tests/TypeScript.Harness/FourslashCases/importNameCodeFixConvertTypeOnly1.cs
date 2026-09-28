// ported from: internal/fourslash/tests/importNameCodeFixConvertTypeOnly1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFixConvertTypeOnly1Case
{
    public static void TestImportNameCodeFixConvertTypeOnly1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /a.ts\nexport class A {}\nexport class B {}\n// @Filename: /b.ts\nimport type { A } from './a';\nnew B"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/b.ts");
                f.VerifyImportFixAtPosition(t, ["import { B, type A } from './a';\nnew B"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
