// ported from: internal/fourslash/tests/genericInterfacesWithConstraints1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GenericInterfacesWithConstraints1Case
{
    public static void TestGenericInterfacesWithConstraints1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface A { a: string; }\ninterface B extends A { b: string; }\ninterface C extends B { c: string; }\ninterface G<T, U extends B> {\n    x: T;\n    y: U;\n}\nvar v/*1*/1: G<A, C>;               // Ok\nvar v/*2*/2: G<{ a: string }, C>;   // Ok, equivalent to G<A, C>\nvar v/*3*/3: G<G<A, B>, C>;         // Ok"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "var v1: G<A, C>", "");
                f.VerifyQuickInfoAt(t, "2", "var v2: G<{\n    a: string;\n}, C>", "");
                f.VerifyQuickInfoAt(t, "3", "var v3: G<G<A, B>, C>", "");
            }
            finally
            {
                done();
            }
        });
    }

}
