// ported from: internal/fourslash/tests/tsxGoToDefinitionUnionElementType1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class TsxGoToDefinitionUnionElementType1Case
{
    public static void TestTsxGoToDefinitionUnionElementType1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("//@Filename: file.tsx\n// @jsx: preserve\n// @noLib: true\ndeclare namespace JSX {\n    interface Element { }\n    interface IntrinsicElements {\n    }\n    interface ElementAttributesProperty { props; }\n}\nfunction /*pt1*/SFC1(prop: { x: number }) {\n    return <div>hello </div>;\n};\nfunction SFC2(prop: { x: boolean }) {\n    return <h1>World </h1>;\n}\nvar /*def*/SFCComp = SFC1 || SFC2;\n<[|SFC/*one*/Comp|] x />"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "one");
            }
            finally
            {
                done();
            }
        });
    }

}
