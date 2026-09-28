// ported from: internal/fourslash/tests/unusedClassInNamespace4_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class UnusedClassInNamespace4Case
{
    public static void TestUnusedClassInNamespace4(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: false\n// @noUnusedLocals: true\n// @noUnusedParameters:true\n [| namespace Validation {\n    class c1 {\n\n    }\n\n    export class c2 {\n\n    }\n\n    class c3 {\n        public x: c1;\n    }\n} |]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyRangeAfterCodeFix(t, "namespace Validation {\n    class c1 {\n\n    }\n\n    export class c2 {\n\n    }\n}", false, 0, 0);
            }
            finally
            {
                done();
            }
        });
    }

}
