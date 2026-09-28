// ported from: internal/fourslash/tests/goToTypeDefinition5_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToTypeDefinition5Case
{
    public static void TestGoToTypeDefinition5(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = "// @Filename: foo.ts\nlet Foo: /*definition*/unresolved;\ntype Foo = { x: string };\n/*reference*/Foo;";
            var (f, done) = F.Fourslash.NewFourslash(t, null, content);
            try
            {
                f.VerifyBaselineGoToTypeDefinition(t, "reference");
            }
            finally
            {
                done();
            }
        });
    }

}
