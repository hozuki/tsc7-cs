// ported from: internal/fourslash/tests/autoImportModuleAugmentation_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportModuleAugmentationCase
{
    public static void TestAutoImportModuleAugmentation(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /a.ts\nexport interface Foo {\n    x: number;\n}\n\n// @Filename: /b.ts\nexport {};\ndeclare module \"./a\" {\n    export const Foo: any;\n}\n\n// @Filename: /c.ts\nFoo/**/\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.BaselineAutoImportsCompletions(t, [""]);
            }
            finally
            {
                done();
            }
        });
    }

}
