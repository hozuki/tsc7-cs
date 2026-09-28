// ported from: internal/fourslash/tests/quickinfoVerbosity3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickinfoVerbosity3Case
{
    public static void TestQuickinfoVerbosity3(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /a.ts\nexport type X = { x: number };\nexport function f(x: X): void {}\n// @Filename: /b.ts\nimport { f } from \"./a\";\n/*1*/f({ x: 1 });\n// @Filename: file.tsx\n// @jsx: preserve\n// @noLib: true\n interface OptionProp {\n     propx: 2\n }\n class Opt extends React.Component<OptionProp, {}> {\n     render() {\n         return <div>Hello</div>;\n     }\n }\n const obj1: OptionProp = {\n     propx: 2\n }\n let y1 = <Opt/*2*/ propx={2} />;\n// @Filename: a.ts\n interface Foo/*3*/<T extends Date> {\n     prop: T\n }\n class Bar/*4*/<T extends Date> implements Foo<T> {\n     prop!: T\n }\n// @Filename: c.ts\n class c5b { public foo() { } }\n namespace c5b/*5*/ { export var y = 2; }"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHoverWithVerbosity(t, new Dictionary<string, int[]> { ["1"] = [0, 1], ["2"] = [0, 1, 2], ["3"] = [0, 1], ["4"] = [0, 1, 2], ["5"] = [0, 1, 2] });
            }
            finally
            {
                done();
            }
        });
    }

}
