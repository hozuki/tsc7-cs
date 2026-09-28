// ported from: internal/fourslash/tests/importNameCodeFixDefaultExport4_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFixDefaultExport4Case
{
    public static void TestImportNameCodeFixDefaultExport4(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /foo.ts\nconst a = () => {};\nexport default a;\n// @Filename: /test.ts\n[|foo|];"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/test.ts");
                f.VerifyImportFixAtPosition(t, ["import foo from \"./foo\";\n\nfoo"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
