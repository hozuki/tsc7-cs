// ported from: internal/fourslash/tests/quickInfoInOptionalChain_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoInOptionalChainCase
{
    public static void TestQuickInfoInOptionalChain(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: true\ninterface A {\n  arr: string[];\n}\n\nfunction test(a?: A): string {\n  return a?.ar/*1*/r.length ? \"A\" : \"B\";\n}\n\ninterface Foo { bar: { baz: string } };\ndeclare const foo: Foo | undefined;\n\nif (foo?.b/*2*/ar.b/*3*/az) {}\n\ninterface Foo2 { bar?: { baz: { qwe: string } } };\ndeclare const foo2: Foo2;\n\nif (foo2.b/*4*/ar?.b/*5*/az.q/*6*/we) {}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(property) A.arr: string[]", "");
                f.VerifyQuickInfoAt(t, "2", "(property) Foo.bar: {\n    baz: string;\n}", "");
                f.VerifyQuickInfoAt(t, "3", "(property) baz: string | undefined", "");
                f.VerifyQuickInfoAt(t, "4", "(property) Foo2.bar?: {\n    baz: {\n        qwe: string;\n    };\n} | undefined", "");
                f.VerifyQuickInfoAt(t, "5", "(property) baz: {\n    qwe: string;\n}", "");
                f.VerifyQuickInfoAt(t, "6", "(property) qwe: string | undefined", "");
            }
            finally
            {
                done();
            }
        });
    }

}
