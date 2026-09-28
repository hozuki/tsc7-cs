// ported from: internal/fourslash/tests/importNameCodeFixDefaultExport5_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFixDefaultExport5Case
{
    public static void TestImportNameCodeFixDefaultExport5(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /node_modules/hooks/useFoo.ts\ndeclare const _default: () => void;\nexport default _default;\n// @Filename: /test.ts\n[|useFoo|];"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.Configure(t, new LSUP.UserPreferences { AutoImportEntrypointDirectorySearch = TRI.TSTrue });
                f.GoToFile(t, "/test.ts");
                f.VerifyImportFixAtPosition(t, ["import useFoo from \"hooks/useFoo\";\n\nuseFoo"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
