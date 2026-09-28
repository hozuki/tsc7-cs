// ported from: internal/fourslash/tests/typedefinition01_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class Typedefinition01Case
{
    public static void TestTypedefinition01(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n// @Filename: b.ts\nimport n = require('./a');\nvar x/*1*/ = new n.Foo();\n// @Filename: a.ts\nexport class /*2*/Foo {}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.VerifyBaselineGoToTypeDefinition(t, "1");
            }
            finally
            {
                done();
            }
        });
    }

}
