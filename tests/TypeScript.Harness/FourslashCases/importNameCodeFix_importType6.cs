// ported from: internal/fourslash/tests/importNameCodeFix_importType6_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFix_importType6Case
{
    public static void TestImportNameCodeFix_importType6(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: es2015\n// @esModuleInterop: true\n// @jsx: react\n// @Filename: /types.d.ts\ndeclare module \"react\" { var React: any; export = React; export as namespace React; }\n// @Filename: /a.tsx\nimport type React from \"react\";\nfunction Component() {}\n(<Component/**/ />)"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifyImportFixAtPosition(t, ["import React from \"react\";\nfunction Component() {}\n(<Component />)"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
