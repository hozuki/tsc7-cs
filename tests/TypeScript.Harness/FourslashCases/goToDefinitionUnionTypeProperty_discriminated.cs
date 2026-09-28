// ported from: internal/fourslash/tests/goToDefinitionUnionTypeProperty_discriminated_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionUnionTypeProperty_discriminatedCase
{
    public static void TestGoToDefinitionUnionTypeProperty_discriminated(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("type U = A | B;\n\ninterface A {\n  /*aKind*/kind: \"a\";\n  /*aProp*/prop: number;\n};\n\ninterface B {\n  /*bKind*/kind: \"b\";\n  /*bProp*/prop: string;\n}\n\nconst u: U = {\n  [|/*kind*/kind|]: \"a\",\n  [|/*prop*/prop|]: 0,\n};\nconst u2: U = {\n  [|/*kindBogus*/kind|]: \"bogus\",\n  [|/*propBogus*/prop|]: 0,\n};"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "kind", "prop", "kindBogus", "propBogus");
            }
            finally
            {
                done();
            }
        });
    }

}
