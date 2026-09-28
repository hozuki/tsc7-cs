// ported from: internal/fourslash/tests/genericInterfacePropertyInference1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GenericInterfacePropertyInference1Case
{
    public static void TestGenericInterfacePropertyInference1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: false\ninterface I {\n    x: number;\n}\n\nvar anInterface: I;\ninterface IG<T> {\n    x: T;\n}\nvar aGenericInterface: IG<number>;\n\nclass C<T> implements IG<T> {\n    x: T;\n}\n\ninterface Foo<T> {\n    prim1: number;\n    prim2: string;\n    ofT: T;\n    ofFooNum: Foo<number>;\n    ofInterface: I;\n    ofIG4: { x: number };\n    ofIG6: { x: T };\n    ofC2: C<number>;\n    ofC4: C<{ x: T }>\n}\n\nvar f: Foo<any>;\nvar f2: Foo<number>;\nvar f3: Foo<I>;\nvar f4: Foo<{ x: number }>;\nvar f5: Foo<Foo<number>>;\n\n// T is any\nvar f_/*a1*/r1  = f.prim1;\nvar f_/*a2*/r2  = f.prim2;\nvar f_/*a3*/r3  = f.ofT;\nvar f_/*a4*/r5  = f.ofFooNum;\nvar f_/*a5*/r8  = f.ofInterface;\nvar f_/*a6*/r12 = f.ofIG4;\nvar f_/*a7*/r14 = f.ofIG6;\nvar f_/*a8*/r18 = f.ofC2;\nvar f_/*a9*/r20 = f.ofC4;\n\n// T is number\nvar f2_/*b1*/r1  = f2.prim1;\nvar f2_/*b2*/r2  = f2.prim2;\nvar f2_/*b3*/r3  = f2.ofT;\nvar f2_/*b4*/r5  = f2.ofFooNum;\nvar f2_/*b5*/r8  = f2.ofInterface;\nvar f2_/*b6*/r12 = f2.ofIG4;\nvar f2_/*b7*/r14 = f2.ofIG6;\nvar f2_/*b8*/r18 = f2.ofC2;\nvar f2_/*b9*/r20 = f2.ofC4;\n\n// T is I\nvar f3_/*c1*/r1  = f3.prim1;\nvar f3_/*c2*/r2  = f3.prim2;\nvar f3_/*c3*/r3  = f3.ofT;\nvar f3_/*c4*/r5  = f3.ofFooNum;\nvar f3_/*c5*/r8  = f3.ofInterface;\nvar f3_/*c6*/r12 = f3.ofIG4;\nvar f3_/*c7*/r14 = f3.ofIG6;\nvar f3_/*c8*/r18 = f3.ofC2;\nvar f3_/*c9*/r20 = f3.ofC4;\n\n// T is {x: number}\nvar f4_/*d1*/r1 =  f4.prim1;\nvar f4_/*d2*/r2 =  f4.prim2;\nvar f4_/*d3*/r3 =  f4.ofT;\nvar f4_/*d4*/r5 =  f4.ofFooNum;\nvar f4_/*d5*/r8 =  f4.ofInterface;\nvar f4_/*d6*/r12 = f4.ofIG4;\nvar f4_/*d7*/r14 = f4.ofIG6;\nvar f4_/*d8*/r18 = f4.ofC2;\nvar f4_/*d9*/r20 = f4.ofC4;\n\n// T is Foo<number>\nvar f5_/*e1*/r1  = f5.prim1;\nvar f5_/*e2*/r2  = f5.prim2;\nvar f5_/*e3*/r3  = f5.ofT;\nvar f5_/*e4*/r5  = f5.ofFooNum;\nvar f5_/*e5*/r8  = f5.ofInterface;\nvar f5_/*e6*/r12 = f5.ofIG4;\nvar f5_/*e7*/r14 = f5.ofIG6;\nvar f5_/*e8*/r18 = f5.ofC2;\nvar f5_/*e9*/r20 = f5.ofC4;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNoErrors(t);
                f.VerifyQuickInfoAt(t, "a1", "var f_r1: number", "");
                f.VerifyQuickInfoAt(t, "a2", "var f_r2: string", "");
                f.VerifyQuickInfoAt(t, "a3", "var f_r3: any", "");
                f.VerifyQuickInfoAt(t, "a4", "var f_r5: Foo<number>", "");
                f.VerifyQuickInfoAt(t, "a5", "var f_r8: I", "");
                f.VerifyQuickInfoAt(t, "a6", "var f_r12: {\n    x: number;\n}", "");
                f.VerifyQuickInfoAt(t, "a7", "var f_r14: {\n    x: any;\n}", "");
                f.VerifyQuickInfoAt(t, "a8", "var f_r18: C<number>", "");
                f.VerifyQuickInfoAt(t, "a9", "var f_r20: C<{\n    x: any;\n}>", "");
                f.VerifyQuickInfoAt(t, "b1", "var f2_r1: number", "");
                f.VerifyQuickInfoAt(t, "b2", "var f2_r2: string", "");
                f.VerifyQuickInfoAt(t, "b3", "var f2_r3: number", "");
                f.VerifyQuickInfoAt(t, "b4", "var f2_r5: Foo<number>", "");
                f.VerifyQuickInfoAt(t, "b5", "var f2_r8: I", "");
                f.VerifyQuickInfoAt(t, "b6", "var f2_r12: {\n    x: number;\n}", "");
                f.VerifyQuickInfoAt(t, "b7", "var f2_r14: {\n    x: number;\n}", "");
                f.VerifyQuickInfoAt(t, "b8", "var f2_r18: C<number>", "");
                f.VerifyQuickInfoAt(t, "b9", "var f2_r20: C<{\n    x: number;\n}>", "");
                f.VerifyQuickInfoAt(t, "c1", "var f3_r1: number", "");
                f.VerifyQuickInfoAt(t, "c2", "var f3_r2: string", "");
                f.VerifyQuickInfoAt(t, "c3", "var f3_r3: I", "");
                f.VerifyQuickInfoAt(t, "c4", "var f3_r5: Foo<number>", "");
                f.VerifyQuickInfoAt(t, "c5", "var f3_r8: I", "");
                f.VerifyQuickInfoAt(t, "c6", "var f3_r12: {\n    x: number;\n}", "");
                f.VerifyQuickInfoAt(t, "c7", "var f3_r14: {\n    x: I;\n}", "");
                f.VerifyQuickInfoAt(t, "c8", "var f3_r18: C<number>", "");
                f.VerifyQuickInfoAt(t, "c9", "var f3_r20: C<{\n    x: I;\n}>", "");
                f.VerifyQuickInfoAt(t, "d1", "var f4_r1: number", "");
                f.VerifyQuickInfoAt(t, "d2", "var f4_r2: string", "");
                f.VerifyQuickInfoAt(t, "d3", "var f4_r3: {\n    x: number;\n}", "");
                f.VerifyQuickInfoAt(t, "d4", "var f4_r5: Foo<number>", "");
                f.VerifyQuickInfoAt(t, "d5", "var f4_r8: I", "");
                f.VerifyQuickInfoAt(t, "d6", "var f4_r12: {\n    x: number;\n}", "");
                f.VerifyQuickInfoAt(t, "d7", "var f4_r14: {\n    x: {\n        x: number;\n    };\n}", "");
                f.VerifyQuickInfoAt(t, "d8", "var f4_r18: C<number>", "");
                f.VerifyQuickInfoAt(t, "d9", "var f4_r20: C<{\n    x: {\n        x: number;\n    };\n}>", "");
                f.VerifyQuickInfoAt(t, "e1", "var f5_r1: number", "");
                f.VerifyQuickInfoAt(t, "e2", "var f5_r2: string", "");
                f.VerifyQuickInfoAt(t, "e3", "var f5_r3: Foo<number>", "");
                f.VerifyQuickInfoAt(t, "e4", "var f5_r5: Foo<number>", "");
                f.VerifyQuickInfoAt(t, "e5", "var f5_r8: I", "");
                f.VerifyQuickInfoAt(t, "e6", "var f5_r12: {\n    x: number;\n}", "");
                f.VerifyQuickInfoAt(t, "e7", "var f5_r14: {\n    x: Foo<number>;\n}", "");
                f.VerifyQuickInfoAt(t, "e8", "var f5_r18: C<number>", "");
                f.VerifyQuickInfoAt(t, "e9", "var f5_r20: C<{\n    x: Foo<number>;\n}>", "");
            }
            finally
            {
                done();
            }
        });
    }

}
