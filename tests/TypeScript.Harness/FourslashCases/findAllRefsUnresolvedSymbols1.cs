// ported from: internal/fourslash/tests/findAllRefsUnresolvedSymbols1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsUnresolvedSymbols1Case
{
    public static void TestFindAllRefsUnresolvedSymbols1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("let a: /*a0*/Bar;\nlet b: /*a1*/Bar<string>;\nlet c: /*a2*/Bar<string, number>;\nlet d: /*b0*/Bar./*c0*/X;\nlet e: /*b1*/Bar./*c1*/X<string>;\nlet f: /*b2*/Bar./*d0*/X./*e0*/Y;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "a0", "a1", "a2", "b0", "b1", "b2", "c0", "c1", "d0", "e0");
            }
            finally
            {
                done();
            }
        });
    }

}
