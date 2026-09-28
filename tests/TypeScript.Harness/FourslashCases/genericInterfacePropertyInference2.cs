// ported from: internal/fourslash/tests/genericInterfacePropertyInference2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GenericInterfacePropertyInference2Case
{
    public static void TestGenericInterfacePropertyInference2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: false\ninterface I {\n    x: number;\n}\n\nvar anInterface: I;\ninterface IG<T> {\n    x: T;\n}\nvar aGenericInterface: IG<number>;\n\nclass C<T> implements IG<T> {\n    x: T;\n}\n\ninterface Foo<T> {\n    ofFooT: Foo<T>;\n    ofFooFooNum: Foo<Foo<number>>; // should be error?\n    ofIG: IG<T>;\n    ofIG5: { x: Foo<T>; }; // same as ofIG3\n    ofC1: C<T>;\n}\n\nvar f: Foo<any>;\nvar f2: Foo<number>;\nvar f3: Foo<I>;\nvar f4: Foo<{ x: number }>;\nvar f5: Foo<Foo<number>>;\n\n// T is any\nvar f_/*a1*/r4 = f.ofFooT;\nvar f_/*a2*/r7 = f.ofFooFooNum;\nvar f_/*a3*/r9 = f.ofIG;\nvar f_/*a5*/r13 = f.ofIG5;\nvar f_/*a7*/r17 = f.ofC1;\n\n// T is number\nvar f2_/*b1*/r4 = f2.ofFooT;\nvar f2_/*b2*/r7 = f2.ofFooFooNum;\nvar f2_/*b3*/r9 = f2.ofIG;\nvar f2_/*b5*/r13 = f2.ofIG5;\nvar f2_/*b7*/r17 = f2.ofC1;\n\n// T is I}\nvar f3_/*c1*/r4 = f3.ofFooT;\nvar f3_/*c2*/r7 = f3.ofFooFooNum;\nvar f3_/*c3*/r9 = f3.ofIG;\nvar f3_/*c5*/r13 = f3.ofIG5;\nvar f3_/*c7*/r17 = f3.ofC1;\n\n// T is {x: number}\nvar f4_/*d1*/r4 = f4.ofFooT;\nvar f4_/*d2*/r7 = f4.ofFooFooNum;\nvar f4_/*d3*/r9 = f4.ofIG;\nvar f4_/*d5*/r13 = f4.ofIG5;\nvar f4_/*d7*/r17 = f4.ofC1;\n\n// T is Foo<number>\nvar f5_/*e1*/r4 = f5.ofFooT;\nvar f5_/*e2*/r7 = f5.ofFooFooNum;\nvar f5_/*e3*/r9 = f5.ofIG;\nvar f5_/*e5*/r13 = f5.ofIG5;\nvar f5_/*e7*/r17 = f5.ofC1;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNoErrors(t);
                f.VerifyQuickInfoAt(t, "a1", "var f_r4: Foo<any>", "");
                f.VerifyQuickInfoAt(t, "a2", "var f_r7: Foo<Foo<number>>", "");
                f.VerifyQuickInfoAt(t, "a3", "var f_r9: IG<any>", "");
                f.VerifyQuickInfoAt(t, "a5", "var f_r13: {\n    x: Foo<any>;\n}", "");
                f.VerifyQuickInfoAt(t, "a7", "var f_r17: C<any>", "");
                f.VerifyQuickInfoAt(t, "b1", "var f2_r4: Foo<number>", "");
                f.VerifyQuickInfoAt(t, "b2", "var f2_r7: Foo<Foo<number>>", "");
                f.VerifyQuickInfoAt(t, "b3", "var f2_r9: IG<number>", "");
                f.VerifyQuickInfoAt(t, "b5", "var f2_r13: {\n    x: Foo<number>;\n}", "");
                f.VerifyQuickInfoAt(t, "b7", "var f2_r17: C<number>", "");
                f.VerifyQuickInfoAt(t, "c1", "var f3_r4: Foo<I>", "");
                f.VerifyQuickInfoAt(t, "c2", "var f3_r7: Foo<Foo<number>>", "");
                f.VerifyQuickInfoAt(t, "c3", "var f3_r9: IG<I>", "");
                f.VerifyQuickInfoAt(t, "c5", "var f3_r13: {\n    x: Foo<I>;\n}", "");
                f.VerifyQuickInfoAt(t, "c7", "var f3_r17: C<I>", "");
                f.VerifyQuickInfoAt(t, "d1", "var f4_r4: Foo<{\n    x: number;\n}>", "");
                f.VerifyQuickInfoAt(t, "d2", "var f4_r7: Foo<Foo<number>>", "");
                f.VerifyQuickInfoAt(t, "d3", "var f4_r9: IG<{\n    x: number;\n}>", "");
                f.VerifyQuickInfoAt(t, "d5", "var f4_r13: {\n    x: Foo<{\n        x: number;\n    }>;\n}", "");
                f.VerifyQuickInfoAt(t, "d7", "var f4_r17: C<{\n    x: number;\n}>", "");
                f.VerifyQuickInfoAt(t, "e1", "var f5_r4: Foo<Foo<number>>", "");
                f.VerifyQuickInfoAt(t, "e2", "var f5_r7: Foo<Foo<number>>", "");
                f.VerifyQuickInfoAt(t, "e3", "var f5_r9: IG<Foo<number>>", "");
                f.VerifyQuickInfoAt(t, "e5", "var f5_r13: {\n    x: Foo<Foo<number>>;\n}", "");
                f.VerifyQuickInfoAt(t, "e7", "var f5_r17: C<Foo<number>>", "");
            }
            finally
            {
                done();
            }
        });
    }

}
