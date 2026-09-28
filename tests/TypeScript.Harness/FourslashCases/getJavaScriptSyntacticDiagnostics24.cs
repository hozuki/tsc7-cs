// ported from: internal/fourslash/tests/getJavaScriptSyntacticDiagnostics24_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetJavaScriptSyntacticDiagnostics24Case
{
    public static void TestGetJavaScriptSyntacticDiagnostics24(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: a.js\nfunction Person(age) {\n    if (age >= 18) {\n        this.canVote = true;\n    } else {\n        this.canVote = 23;\n    }\n}\nlet x = new Person(100);\nx.canVote/**/;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "", "(property) Person.canVote: number | boolean", "");
            }
            finally
            {
                done();
            }
        });
    }

}
