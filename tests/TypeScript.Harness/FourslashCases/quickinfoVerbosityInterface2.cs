// ported from: internal/fourslash/tests/quickinfoVerbosityInterface2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickinfoVerbosityInterface2Case
{
    public static void TestQuickinfoVerbosityInterface2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("{\n    interface Foo/*1*/ {\n        a: \"a\" | \"c\";\n    }\n}\n{\n    interface Bar {\n        b: \"b\" | \"d\";\n    }\n    interface Foo/*2*/ extends Bar {\n        a: \"a\" | \"c\";\n    }\n}\n{\n    type BarParam = \"b\" | \"d\";\n    interface Bar {\n        bar(b: BarParam): string;\n    }\n    type FooType = \"a\" | \"c\";\n    interface FooParam {\n        param: FooType;\n    }\n    interface Foo/*3*/ extends Bar {\n        a: FooType;\n        foo: (a: FooParam) => number;\n    }\n}\n{\n    interface Bar/*4*/<B> {\n        bar(b: B): string;\n    }\n    interface FooParam {\n        param: \"a\" | \"c\";\n    }\n    interface Foo/*5*/ extends Bar<FooParam> {\n        a: \"a\" | \"c\";\n        foo: (a: FooParam) => number;\n    }\n}\n{\n    interface Foo {\n        a: \"a\";\n    }\n    interface Foo/*6*/ {\n        b: \"b\";\n    }\n}\ninterface Foo/*7*/ {\n    a: \"a\";\n}\nnamespace Foo/*8*/ {\n    export const bar: string;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHoverWithVerbosity(t, new Dictionary<string, int[]> { ["1"] = [0, 1], ["2"] = [0, 1], ["3"] = [0, 1, 2], ["4"] = [0, 1], ["5"] = [0, 1, 2], ["6"] = [0, 1], ["7"] = [0, 1], ["8"] = [0, 1] });
            }
            finally
            {
                done();
            }
        });
    }

}
