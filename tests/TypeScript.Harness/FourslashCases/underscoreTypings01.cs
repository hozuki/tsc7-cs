// ported from: internal/fourslash/tests/underscoreTypings01_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class UnderscoreTypings01Case
{
    public static void TestUnderscoreTypings01(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Iterator_<T, U> {\n    (value: T, index: any, list: any): U;\n}\n\ninterface WrappedArray<T> {\n    map<U>(iterator: Iterator_<T, U>, context?: any): U[];\n}\n\ninterface Underscore {\n    <T>(list: T[]): WrappedArray<T>;\n    map<T, U>(list: T[], iterator: Iterator_<T, U>, context?: any): U[];\n}\n\ndeclare var _: Underscore;\n\nvar a: string[];\nvar /*1*/b = _.map(a, /*2*/x => x.length);    // Was typed any[], should be number[]\nvar /*3*/c = _(a).map(/*4*/x => x.length);\nvar /*5*/d = a.map(/*6*/x => x.length);\n\nvar aa: any[];\nvar /*7*/bb = _.map(aa, /*8*/x => x.length);\nvar /*9*/cc = _(aa).map(/*10*/x => x.length);\nvar /*11*/dd = aa.map(/*12*/x => x.length);\n\nvar e = a.map(x => x./*13*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "var b: number[]", "");
                f.VerifyQuickInfoAt(t, "2", "(parameter) x: string", "");
                f.VerifyQuickInfoAt(t, "3", "var c: number[]", "");
                f.VerifyQuickInfoAt(t, "4", "(parameter) x: string", "");
                f.VerifyQuickInfoAt(t, "5", "var d: number[]", "");
                f.VerifyQuickInfoAt(t, "6", "(parameter) x: string", "");
                f.VerifyQuickInfoAt(t, "7", "var bb: any[]", "");
                f.VerifyQuickInfoAt(t, "8", "(parameter) x: any", "");
                f.VerifyQuickInfoAt(t, "9", "var cc: any[]", "");
                f.VerifyQuickInfoAt(t, "10", "(parameter) x: any", "");
                f.VerifyQuickInfoAt(t, "11", "var dd: any[]", "");
                f.VerifyQuickInfoAt(t, "12", "(parameter) x: any", "");
                f.VerifyCompletions(t, "13", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["length"], Excludes = ["toFixed"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
