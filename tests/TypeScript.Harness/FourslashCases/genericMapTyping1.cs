// ported from: internal/fourslash/tests/genericMapTyping1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GenericMapTyping1Case
{
    public static void TestGenericMapTyping1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: false\ninterface Iterator_<T, U> {\n    (value: T, index: any, list: any): U;\n}\ninterface WrappedArray<T> {\n    map<U>(iterator: Iterator_<T, U>, context?: any): U[];\n}\ninterface Underscore {\n    <T>(list: T[]): WrappedArray<T>;\n    map<T, U>(list: T[], iterator: Iterator_<T, U>, context?: any): U[];\n}\ndeclare var _: Underscore;\nvar aa: string[];\nvar b/*1*/b = _.map(aa, x/*7*/x => xx.length);    // should be number[]\nvar c/*2*/c = _(aa).map(x/*8*/x => xx.length);    // should be number[]\nvar d/*3*/d = aa.map(xx => x/*9*/x.length);       // should be number[]\nvar aaa: any[];\nvar b/*4*/bb = _.map(aaa, xx => xx.length); // should be any[]\nvar c/*5*/cc = _(aaa).map(xx => xx.length);  // Should not error, should be any[]\nvar d/*6*/dd = aaa.map(xx => xx.length);     // should not error, should be any[]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNoErrors(t);
                f.VerifyQuickInfoAt(t, "1", "var bb: number[]", "");
                f.VerifyQuickInfoAt(t, "2", "var cc: number[]", "");
                f.VerifyQuickInfoAt(t, "3", "var dd: number[]", "");
                f.VerifyQuickInfoAt(t, "4", "var bbb: any[]", "");
                f.VerifyQuickInfoAt(t, "5", "var ccc: any[]", "");
                f.VerifyQuickInfoAt(t, "6", "var ddd: any[]", "");
                f.VerifyQuickInfoAt(t, "7", "(parameter) xx: string", "");
                f.VerifyQuickInfoAt(t, "8", "(parameter) xx: string", "");
                f.VerifyQuickInfoAt(t, "9", "(parameter) xx: string", "");
            }
            finally
            {
                done();
            }
        });
    }

}
