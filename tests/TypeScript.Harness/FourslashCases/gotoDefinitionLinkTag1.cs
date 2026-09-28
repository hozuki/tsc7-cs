// ported from: internal/fourslash/tests/gotoDefinitionLinkTag1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GotoDefinitionLinkTag1Case
{
    public static void TestGotoDefinitionLinkTag1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: foo.ts\ninterface [|/*def1*/Foo|] {\n    foo: string\n}\nnamespace NS {\n    export interface [|/*def2*/Bar|] {\n        baz: Foo\n    }\n}\n/** {@link /*use1*/[|Foo|]} foooo*/\nconst a = \"\"\n/** {@link NS./*use2*/[|Bar|]} ns.bar*/\nconst b = \"\"\n/** {@link /*use3*/[|Foo|] f1}*/\nconst c = \"\"\n/** {@link NS./*use4*/[|Bar|] ns.bar}*/\nconst [|/*def3*/d|] = \"\"\n/** {@link /*use5*/[|d|] }dd*/\nconst e = \"\"\n/** @param x {@link /*use6*/[|Foo|]} */\nfunction foo(x) { }\n// @Filename: bar.ts\n/** {@link /*use7*/[|Foo|] }dd*/\nconst f = \"\""u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, false, "use1", "use2", "use3", "use4", "use5", "use6", "use7");
            }
            finally
            {
                done();
            }
        });
    }

}
