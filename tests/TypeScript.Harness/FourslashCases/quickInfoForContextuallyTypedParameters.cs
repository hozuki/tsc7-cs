// ported from: internal/fourslash/tests/quickInfoForContextuallyTypedParameters_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoForContextuallyTypedParametersCase
{
    public static void TestQuickInfoForContextuallyTypedParameters(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("declare function foo1<T>(obj: T, settings: (row: T) => { value: string, func?: Function }): void;\n\nfoo1(new Error(),\n    o/*1*/ => ({\n        value: o.name,\n        func: x => 'foo'\n    })\n);\n\ndeclare function foo2<T>(settings: (row: T) => { value: string, func?: Function }, obj: T): void;\n\nfoo2(o/*2*/ => ({\n        value: o.name,\n        func: x => 'foo'\n    }),\n    new Error(),\n);\n\ndeclare function foof<T extends { name: string }, U extends keyof T>(settings: (row: T) => { value: T[U], func?: Function }, obj: T, key: U): U;\n\nfunction q<T extends { name: string }>(x: T): T[\"name\"] {\n    return foof/*3*/(o => ({ value: o.name, func: x => 'foo' }), x, \"name\");\n}\n\nfoof/*4*/(o => ({ value: o.name, func: x => 'foo' }), new Error(), \"name\");"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(parameter) o: Error", "");
                f.VerifyQuickInfoAt(t, "2", "(parameter) o: Error", "");
                f.VerifyQuickInfoAt(t, "3", "function foof<T, \"name\">(settings: (row: T) => {\n    value: T[\"name\"];\n    func?: Function;\n}, obj: T, key: \"name\"): \"name\"", "");
                f.VerifyQuickInfoAt(t, "4", "function foof<Error, \"name\">(settings: (row: Error) => {\n    value: string;\n    func?: Function;\n}, obj: Error, key: \"name\"): \"name\"", "");
            }
            finally
            {
                done();
            }
        });
    }

}
