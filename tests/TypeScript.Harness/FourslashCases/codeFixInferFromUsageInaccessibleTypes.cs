// ported from: internal/fourslash/tests/codeFixInferFromUsageInaccessibleTypes_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixInferFromUsageInaccessibleTypesCase
{
    public static void TestCodeFixInferFromUsageInaccessibleTypes(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: false\n// @noImplicitAny: true\nfunction f1(a) { a; }\nfunction h1() {\n    class C { p: number };\n    f1({ ofTypeC: new C() });\n}\n\nfunction f2(a) { a; }\nfunction h2() {\n    interface I { a: number }\n    var i: I = {a : 1};\n    f2(i);\n    f2(2);\n    f2(false);\n}\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFixNotAvailable(t);
            }
            finally
            {
                done();
            }
        });
    }

}
