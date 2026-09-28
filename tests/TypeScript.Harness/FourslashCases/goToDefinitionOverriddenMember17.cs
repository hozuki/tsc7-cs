// ported from: internal/fourslash/tests/goToDefinitionOverriddenMember17_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionOverriddenMember17Case
{
    public static void TestGoToDefinitionOverriddenMember17(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: true\n// @target: esnext\n// @lib: esnext\nconst entityKind = Symbol.for(\"drizzle:entityKind\");\n\nabstract class MySqlColumn {\n  static readonly /*2*/[entityKind]: string = \"MySqlColumn\";\n}\n\nexport class MySqlVarBinary extends MySqlColumn {\n  static [|/*1*/override|] readonly [entityKind]: string = \"MySqlVarBinary\";\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "1");
            }
            finally
            {
                done();
            }
        });
    }

}
