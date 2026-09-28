// ported from: internal/fourslash/tests/quickInfoOnMethodOfImportEquals_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoOnMethodOfImportEqualsCase
{
    public static void TestQuickInfoOnMethodOfImportEquals(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /a.d.ts\ndeclare class C<T> {\n    m(): void;\n}\nexport = C;\n// @Filename: /b.ts\nimport C = require(\"./a\");\ndeclare var x: C<number>;\nx./**/m;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "", "(method) C<number>.m(): void", "");
            }
            finally
            {
                done();
            }
        });
    }

}
