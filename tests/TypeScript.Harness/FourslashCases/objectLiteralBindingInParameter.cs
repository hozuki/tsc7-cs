// ported from: internal/fourslash/tests/objectLiteralBindingInParameter_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ObjectLiteralBindingInParameterCase
{
    public static void TestObjectLiteralBindingInParameter(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface I { x1: number; x2: string }\nfunction f(cb: (ev: I) => any) { }\nf(({/*1*/}) => 0);\n[<I>null].reduce(({/*2*/}, b) => b);\ninterface Foo {\n    m(x: { x1: number, x2: number }): void;\n    prop: I;\n}\nlet x: Foo = {\n    m({ /*3*/ }) {\n    },\n    get prop(): I { return undefined; },\n    set prop({ /*4*/ }) {\n    }\n};"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, f.Markers(), new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["x1", "x2"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
