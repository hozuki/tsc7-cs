// ported from: internal/fourslash/tests/codeFixAddMissingFunctionDeclaration16_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixAddMissingFunctionDeclaration16Case
{
    public static void TestCodeFixAddMissingFunctionDeclaration16(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @filename: /node_modules/test/index.js\nexport const x = 1;\n// @filename: /foo.ts\nimport * as test from \"test\";\ntest.foo();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/foo.ts");
                f.VerifyCodeFixNotAvailable(t);
            }
            finally
            {
                done();
            }
        });
    }

}
