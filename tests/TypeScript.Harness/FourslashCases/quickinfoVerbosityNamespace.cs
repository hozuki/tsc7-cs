// ported from: internal/fourslash/tests/quickinfoVerbosityNamespace_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickinfoVerbosityNamespaceCase
{
    public static void TestQuickinfoVerbosityNamespace(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @filename: /1.ts\nexport {};\nclass Foo<T> {\n    y: string;\n}\nnamespace Foo/*1*/ {\n    export var y: number = 1;\n    export var x: string = \"hello\";\n    export var w = \"world\";\n    var z = 2;\n}\n// @filename: /2.ts\nexport namespace Foo {\n    export var y: number = 1;\n    export var x: string = \"hello\";\n}\n// @filename: /3.ts\nimport * as Foo_1 from \"./b\";\nexport declare namespace ns/*2*/ {\n    import Foo = Foo_1.Foo;\n    export { Foo };\n    export const c: number;\n    export const d = 1;\n    let e: Apple;\n    export let f: Apple;\n}\ninterface Apple {\n    a: string;\n}\n// @filename: /4.ts\nclass Foo<T> {\n    y!: T;\n}\nnamespace Two/*3*/ {\n    export const f = new Foo<number>();\n}\n// @filename: /5.ts\nnamespace Two {\n    export const g = new Foo<string>();\n}\n// @filename: /6.ts\nnamespace OnlyLocal/*4*/ {\n    const bar: number;\n}\n// @filename: foo.ts\nexport function foo() { return \"foo\"; }\nimport(\"/*5*/./foo\")\nvar x = import(\"./foo\")"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHoverWithVerbosity(t, new Dictionary<string, int[]> { ["1"] = [0, 1], ["2"] = [0, 1, 2], ["3"] = [0, 1, 2], ["4"] = [0, 1], ["5"] = [0, 1] });
            }
            finally
            {
                done();
            }
        });
    }

}
