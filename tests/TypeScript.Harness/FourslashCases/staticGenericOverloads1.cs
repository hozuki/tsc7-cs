// ported from: internal/fourslash/tests/staticGenericOverloads1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class StaticGenericOverloads1Case
{
    public static void TestStaticGenericOverloads1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class A<T> {\n    static B<S>(v: A<S>): A<S>;\n    static B<S>(v: S): A<S>;\n    static B<S>(v: any): A<S> {\n        return null;\n    }\n}\nvar a = new A<number>();\nA.B(/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { OverloadsCount = 2 });
                f.Insert(t, "a");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "B(v: A<number>): A<number>", OverloadsCount = 2 });
                f.Insert(t, "); A.B(");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "B(v: A<unknown>): A<unknown>", OverloadsCount = 2 });
                f.Insert(t, "a");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "B(v: A<number>): A<number>", OverloadsCount = 2 });
            }
            finally
            {
                done();
            }
        });
    }

}
