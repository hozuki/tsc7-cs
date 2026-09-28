// ported from: internal/fourslash/tests/quickinfoVerbosityClass1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickinfoVerbosityClass1Case
{
    public static void TestQuickinfoVerbosityClass1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("{\n    class Foo {\n        a!: \"a\" | \"c\";\n    }\n    const f/*f1*/ = new Foo();\n}\n{\n    type FooParam = \"a\" | \"b\";\n    class Foo {\n        constructor(public x: string) {\n            this.x = \"a\";\n        }\n        foo(p: FooParam): void {}\n    }\n    const f/*f2*/ = new Foo(\"\");\n}\n{\n    class Bar/*B*/ {\n        a!: string;\n        bar(): void {}\n        baz(param: string): void {}\n    }\n    class Foo extends Bar {\n        b!: boolean;\n        override baz(param: string | number): void {}\n    }\n    const f/*f3*/ = new Foo();\n}\n{\n    class Bar<B extends string> {\n        bar(param: B): void {}\n        baz(): this { return this; }\n    }\n    class Foo extends Bar<\"foo\"> {\n        foo(): this { return this; }\n    }\n    const b/*b1*/ = new Bar();\n    const f/*f4*/ = new Foo();\n}\n{\n    class Bar<B extends string> {\n        bar(param: B): void {}\n        baz(): this { return this; }\n    }\n    const noname/*n1*/ = new (class extends Bar<\"foo\"> {\n        foo(): this { return this; }\n    })();\n    const klass = class extends Bar<\"foo\"> {\n        foo(): this { return this; }\n    };\n    const k/*k1*/ = new klass();\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHoverWithVerbosity(t, new Dictionary<string, int[]> { ["f1"] = [0, 1], ["f2"] = [0, 1, 2], ["f3"] = [0, 1], ["b1"] = [0, 1], ["f4"] = [0, 1], ["n1"] = [0, 1], ["k1"] = [0, 1], ["B"] = [0, 1] });
            }
            finally
            {
                done();
            }
        });
    }

}
