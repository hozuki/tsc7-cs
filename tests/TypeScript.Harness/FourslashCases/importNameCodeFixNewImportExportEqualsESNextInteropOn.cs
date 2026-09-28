// ported from: internal/fourslash/tests/importNameCodeFixNewImportExportEqualsESNextInteropOn_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFixNewImportExportEqualsESNextInteropOnCase
{
    public static void TestImportNameCodeFixNewImportExportEqualsESNextInteropOn(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @EsModuleInterop: true\n// @Module: es2015\n// @Filename: /foo.d.ts\ndeclare module \"foo\" {\n  const foo: number;\n  export = foo;\n}\n// @Filename: /index.ts\n[|foo|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/index.ts");
                f.VerifyImportFixAtPosition(t, ["import foo from \"foo\";\n\nfoo"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
