// ported from: internal/fourslash/tests/processInvalidSyntax1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ProcessInvalidSyntax1Case
{
    public static void TestProcessInvalidSyntax1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: decl.js\nvar obj = {};\n// @Filename: unicode1.js\nobj.\U0001D49C ;\n// @Filename: unicode2.js\nobj.\u00AC ;\n// @Filename: unicode3.js\nobj\u00AC\n// @Filename: forof.js\nfor (obj/**/.prop of arr) {\n\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineRename(t, null!, "");
            }
            finally
            {
                done();
            }
        });
    }

}
