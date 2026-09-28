// ported from: internal/fourslash/tests/getEditsForFileRename_cssImport2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetEditsForFileRename_cssImport2Case
{
    public static void TestGetEditsForFileRename_cssImport2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /tsconfig.json\n{ \"compilerOptions\": { \"allowArbitraryExtensions\": true } }\n// @Filename: /app.css\n.cookie-banner {\n  display: none;\n}\n// @Filename: /app.d.css.ts\ndeclare const css: {\n  cookieBanner: string;\n};\nexport default css;\n// @Filename: /a.ts\nimport styles from \"./app.css\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyWillRenameFilesEdits(t, "/app.d.css.ts", "/app2.d.css.ts", new Dictionary<string, string> { ["/a.ts"] = "import styles from \"./app2.css\";", ["/app2.css"] = ".cookie-banner {\n  display: none;\n}", ["/app2.d.css.ts"] = "declare const css: {\n  cookieBanner: string;\n};\nexport default css;" }, null!);
            }
            finally
            {
                done();
            }
        });
    }

}
