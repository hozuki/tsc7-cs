// ported from: internal/fourslash/tests/reverseMappedTypeQuickInfo_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ReverseMappedTypeQuickInfoCase
{
    public static void TestReverseMappedTypeQuickInfo(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface IAction {\n    type: string;\n}\n\ntype Reducer<S> = (state: S, action: IAction) => S\n\nfunction combineReducers<S>(reducers: { [K in keyof S]: Reducer<S[K]> }): Reducer<S> {\n    const dummy = {} as S;\n    return () => dummy;\n}\n\nconst test_inner = (test: string, action: IAction) => {\n    return 'dummy';\n}\nconst test = combineReducers({\n    test_inner\n});\n\nconst test_outer = combineReducers({\n    test\n});\n\n// '{test: { test_inner: any } }'\ntype FinalType/*1*/ = ReturnType<typeof test_outer>;\n\nvar k: FinalType;\nk.test.test_inner/*2*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "type FinalType = {\n    test: {\n        test_inner: string;\n    };\n}", "");
                f.VerifyQuickInfoAt(t, "2", "(property) test_inner: string", "");
            }
            finally
            {
                done();
            }
        });
    }

}
