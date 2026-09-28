// ported from: internal/fourslash/tests/importFixWithMultipleModuleExportAssignment_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportFixWithMultipleModuleExportAssignmentCase
{
    public static void TestImportFixWithMultipleModuleExportAssignment(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: esnext\n// @allowJs: true\n// @checkJs: true\n// @Filename: /a.js\nfunction f() {}\nmodule.exports = f;\nmodule.exports = 42;\n// @Filename: /b.js\nexport const foo = 0;\n// @Filename: /c.js\nfoo"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/c.js");
                f.VerifyImportFixAtPosition(t, ["const { foo } = require(\"./b\");\n\nfoo"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
