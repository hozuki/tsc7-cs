// ported from: internal/fourslash/tests/importNameCodeFix_importType_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFix_importTypeCase
{
    public static void TestImportNameCodeFix_importType(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @checkJs: true\n// @Filename: /a.js\nexport {};\n/** @typedef {number} T */\n// @Filename: /b.js\n/** @type {T} */\nconst x = 0;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/b.js");
                f.VerifyImportFixAtPosition(t, ["/** @type {import(\"./a\").T} */\nconst x = 0;"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
