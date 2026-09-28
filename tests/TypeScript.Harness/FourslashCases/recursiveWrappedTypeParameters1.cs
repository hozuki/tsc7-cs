// ported from: internal/fourslash/tests/recursiveWrappedTypeParameters1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RecursiveWrappedTypeParameters1Case
{
    public static void TestRecursiveWrappedTypeParameters1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface I<T> {\n\ta: T;\n\tb: I<T>;\n\tc: I<I<T>>;\n}\nvar x: I<number>;\nvar y/*1*/y = x.c.c.c.c.c.b;\nvar a/*2*/a = x.a;\nvar b/*3*/b = x.b;\nvar c/*4*/c = x.c;\nvar d/*5*/d = x.c.a;\nvar e/*6*/e = x.c.b;\nvar f/*7*/f = x.c.c; "u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "var yy: I<I<I<I<I<I<number>>>>>>", "");
                f.VerifyQuickInfoAt(t, "2", "var aa: number", "");
                f.VerifyQuickInfoAt(t, "3", "var bb: I<number>", "");
                f.VerifyQuickInfoAt(t, "4", "var cc: I<I<number>>", "");
                f.VerifyQuickInfoAt(t, "5", "var dd: I<number>", "");
                f.VerifyQuickInfoAt(t, "6", "var ee: I<I<number>>", "");
                f.VerifyQuickInfoAt(t, "7", "var ff: I<I<I<number>>>", "");
            }
            finally
            {
                done();
            }
        });
    }

}
