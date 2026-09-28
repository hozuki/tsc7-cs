// ported from: internal/fourslash/tests/refactorConvertToEsModule_module_nodenext_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RefactorConvertToEsModule_module_nodenextCase
{
    public static void TestRefactorConvertToEsModule_module_nodenext(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @target: esnext\n// @module: node18\n// @Filename: /a.js\nmodule.exports = 0;\n// @Filename: /b.ts\nmodule.exports = 0;\n// @Filename: /c.cjs\nmodule.exports = 0;\n// @Filename: /d.cts\nmodule.exports = 0;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/a.js");
                f.VerifyCodeFixNotAvailable(t);
                f.GoToFile(t, "/b.ts");
                f.VerifyCodeFixNotAvailable(t);
                f.GoToFile(t, "/c.cjs");
                f.VerifyCodeFixNotAvailable(t);
                f.GoToFile(t, "/d.cts");
                f.VerifyCodeFixNotAvailable(t);
            }
            finally
            {
                done();
            }
        });
    }

}
