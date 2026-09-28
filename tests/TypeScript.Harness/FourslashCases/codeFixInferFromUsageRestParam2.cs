// ported from: internal/fourslash/tests/codeFixInferFromUsageRestParam2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixInferFromUsageRestParam2Case
{
    public static void TestCodeFixInferFromUsageRestParam2(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: false\n// @noImplicitAny: true\nfunction f(a: number, [|...rest |]){\n    a; rest;\n}\nf(1);\nf(2, \"s1\");\nf(3, false, \"s2\");\nf(4, \"s1\", \"s2\", false, \"s4\");"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyRangeAfterCodeFix(t, "...rest: (string | boolean)[]", false, 0, 0);
            }
            finally
            {
                done();
            }
        });
    }

}
