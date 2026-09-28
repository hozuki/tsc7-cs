// ported from: internal/fourslash/tests/importNameCodeFixNewImportAmbient2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFixNewImportAmbient2Case
{
    public static void TestImportNameCodeFixNewImportAmbient2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("[|/*!\n * I'm a license or something\n */\nf1/*0*/();|]\n// @Filename: ambientModule.ts\n declare module \"ambient-module\" {\n    export function f1();\n    export var v1;\n }"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixAtPosition(t, ["/*!\n * I'm a license or something\n */\n\nimport { f1 } from \"ambient-module\";\n\nf1();"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
