// ported from: internal/fourslash/tests/codeFixPropertyOverrideAccess4_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixPropertyOverrideAccess4Case
{
    public static void TestCodeFixPropertyOverrideAccess4(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: true\n// @target: esnext\n// @lib: esnext\nconst prop = Symbol.for('foo');\n\nclass A {\n    [prop] = 1;\n}\nclass B extends A {\n    get [prop]() { return 2; }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFixNotAvailable(t, "fixPropertyOverrideAccessor");
            }
            finally
            {
                done();
            }
        });
    }

}
