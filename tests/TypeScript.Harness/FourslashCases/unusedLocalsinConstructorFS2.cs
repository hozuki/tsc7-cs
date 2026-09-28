// ported from: internal/fourslash/tests/unusedLocalsinConstructorFS2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class UnusedLocalsinConstructorFS2Case
{
    public static void TestUnusedLocalsinConstructorFS2(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @noUnusedLocals: true\n// @noUnusedParameters: true\nclass greeter {\n    [|constructor() {\n        var unused = 20;\n        var used = \"dummy\";\n        used = used + \"second part\";\n    }|]\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyRangeAfterCodeFix(t, "\n    constructor() {\n        var used = \"dummy\";\n        used = used + \"second part\";\n    }\n", false, 0, 0);
            }
            finally
            {
                done();
            }
        });
    }

}
