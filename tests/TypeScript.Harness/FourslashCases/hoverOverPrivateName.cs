// ported from: internal/fourslash/tests/hoverOverPrivateName_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class HoverOverPrivateNameCase
{
    public static void TestHoverOverPrivateName(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class A {\n    #f/*1*/oo = 3;\n    #b/*2*/ar: number;\n    #b/*3*/az = () => \"hello\";\n    #q/*4*/ux(n: number): string {\n        return \"\" + n;\n    }\n    static #staticF/*5*/oo = 3;\n    static #staticB/*6*/ar: number;\n    static #staticB/*7*/az = () => \"hello\";\n    static #staticQ/*8*/ux(n: number): string {\n        return \"\" + n;\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(property) A.#foo: number", "");
                f.VerifyQuickInfoAt(t, "2", "(property) A.#bar: number", "");
                f.VerifyQuickInfoAt(t, "3", "(property) A.#baz: () => string", "");
                f.VerifyQuickInfoAt(t, "4", "(method) A.#qux(n: number): string", "");
                f.VerifyQuickInfoAt(t, "5", "(property) A.#staticFoo: number", "");
                f.VerifyQuickInfoAt(t, "6", "(property) A.#staticBar: number", "");
                f.VerifyQuickInfoAt(t, "7", "(property) A.#staticBaz: () => string", "");
                f.VerifyQuickInfoAt(t, "8", "(method) A.#staticQux(n: number): string", "");
            }
            finally
            {
                done();
            }
        });
    }

}
