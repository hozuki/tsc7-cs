// ported from: internal/fourslash/tests/importNameCodeFix_importType5_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFix_importType5Case
{
    public static void TestImportNameCodeFix_importType5(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: es2015\n// @Filename: /exports.ts\nexport interface SomeInterface {}\nexport class SomePig {}\n// @Filename: /a.ts\nimport type { SomeInterface, SomePig } from \"./exports.js\";\nnew SomePig/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifyImportFixAtPosition(t, ["import { SomeInterface, SomePig } from \"./exports.js\";\nnew SomePig"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
