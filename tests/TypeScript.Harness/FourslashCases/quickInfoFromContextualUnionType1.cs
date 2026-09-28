// ported from: internal/fourslash/tests/quickInfoFromContextualUnionType1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoFromContextualUnionType1Case
{
    public static void TestQuickInfoFromContextualUnionType1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: true\n// based on https://github.com/microsoft/TypeScript/issues/55495\ntype X =\n  | {\n      name: string;\n      [key: string]: any;\n    }\n  | {\n      name: \"john\";\n      someProp: boolean;\n    };\n\nconst obj = { name: \"john\", /*1*/someProp: \"foo\" } satisfies X;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(property) someProp: string", "");
            }
            finally
            {
                done();
            }
        });
    }

}
