// ported from: internal/fourslash/tests/importNameCodeFixDefaultExport2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFixDefaultExport2Case
{
    public static void TestImportNameCodeFixDefaultExport2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /lib.ts\nclass Base { }\nexport default Base;\n// @Filename: /test.ts\n[|class Derived extends Base { }|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/test.ts");
                f.VerifyImportFixAtPosition(t, ["import Base from \"./lib\";\n\nclass Derived extends Base { }"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
