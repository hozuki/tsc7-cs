// ported from: internal/fourslash/tests/genericCombinators3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GenericCombinators3Case
{
    public static void TestGenericCombinators3(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Collection<T, U> {\n}\n\ninterface Combinators {\n    map<T, U, V>(c: Collection<T,U>, f: (x: T, y: U) => V): Collection<T, V>;\n    map<T, U>(c: Collection<T,U>, f: (x: T, y: U) => any): Collection<any, any>;\n}\n\nvar c2: Collection<number, string>;\n\nvar _: Combinators;\n\nvar /*9*/r1a  = _.ma/*1c*/p(c2, (/*1a*/x,/*1b*/y) => { return x + \"\" });  // check quick info of map here"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1a", "(parameter) x: number", "");
                f.VerifyQuickInfoAt(t, "1b", "(parameter) y: string", "");
                f.VerifyQuickInfoAt(t, "1c", "(method) Combinators.map<number, string, string>(c: Collection<number, string>, f: (x: number, y: string) => string): Collection<number, string> (+1 overload)", "");
                f.VerifyQuickInfoAt(t, "9", "var r1a: Collection<number, string>", "");
            }
            finally
            {
                done();
            }
        });
    }

}
