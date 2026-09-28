// ported from: internal/fourslash/tests/overloadQuickInfo_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class OverloadQuickInfoCase
{
    public static void TestOverloadQuickInfo(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function Foo(a: string, b: number, c: boolean);\nfunction Foo(a: any, name: string, age: number);\nfunction Foo(fred: any[], name: string, age: number);\nfunction Foo(fred: any[  ] , name: string[], age: number);\nfunction Foo(fred: any[], name: string[], age: number[]);\nfunction Foo(fred:         any, name: string[], age: number[]); // Extraneous spaces should get removed\nfunction Foo(fred: any, name: boolean, age: number[]);\nfunction Foo(dave: boolean, name: string);\nfunction Foo(fred: any, mandy: {(): number}, age: number[]);    // Embedded interface will get converted to shorthand notation, () => \nfunction Foo(fred: any, name: string, age: { });\nfunction Foo(fred: any, name: string, age: number[]);\nfunction Foo(test: string, name, age: number);\nfunction Foo();\nfunction Foo(x?: any, y?: any, z?: any) {\n}\nFo/**/o();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "", "function Foo(): any (+12 overloads)", "");
            }
            finally
            {
                done();
            }
        });
    }

}
