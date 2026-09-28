// ported from: internal/fourslash/tests/quickinfoVerbosityInterface1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickinfoVerbosityInterface1Case
{
    public static void TestQuickinfoVerbosityInterface1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("{\n    interface Foo {\n        a: \"a\" | \"c\";\n    }\n    const f/*f1*/: Foo = { a: \"a\" };\n}\n{\n    interface Bar {\n        b: \"b\" | \"d\";\n    }\n    interface Foo extends Bar {\n        a: \"a\" | \"c\";\n    }\n    const f/*f2*/: Foo = { a: \"a\", b: \"b\" };\n}\n{\n    type BarParam = \"b\" | \"d\";\n    interface Bar {\n        bar(b: BarParam): string;\n    }\n    type FooType = \"a\" | \"c\";\n    interface FooParam {\n        param: FooType;\n    }\n    interface Foo extends Bar {\n        a: FooType;\n        foo: (a: FooParam) => number;\n    }\n    const f/*f3*/: Foo = { a: \"a\", bar: () => \"b\", foo: () => 1 };\n}\n{\n    interface Bar<B> {\n        bar(b: B): string;\n    }\n    interface FooParam {\n        param: \"a\" | \"c\";\n    }\n    interface Foo extends Bar<FooParam> {\n        a: \"a\" | \"c\";\n        foo: (a: FooParam) => number;\n    }\n    const f/*f4*/: Foo = { a: \"a\", bar: () => \"b\", foo: () => 1 };\n    const b/*b1*/: Bar<number> = { bar: () => \"\" };\n}\n{\n    interface Foo<A> {\n        a: A;\n    }\n    type Alias = Foo<string>;\n    const a/*a*/: Alias = { a: \"a\" };\n}\n{\n    interface Foo {\n        a: \"a\";\n    }\n    interface Foo {\n        b: \"b\";\n    }\n    const f/*f5*/: Foo = { a: \"a\", b: \"b\" };\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHoverWithVerbosity(t, new Dictionary<string, int[]> { ["f1"] = [0, 1], ["f2"] = [0, 1], ["f3"] = [0, 1, 2, 3], ["f4"] = [0, 1, 2], ["b1"] = [0, 1], ["a"] = [0, 1, 2], ["f5"] = [0, 1] });
            }
            finally
            {
                done();
            }
        });
    }

}
