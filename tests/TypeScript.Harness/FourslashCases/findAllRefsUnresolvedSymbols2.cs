// ported from: internal/fourslash/tests/findAllRefsUnresolvedSymbols2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsUnresolvedSymbols2Case
{
    public static void TestFindAllRefsUnresolvedSymbols2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("import { /*a0*/Bar } from \"does-not-exist\";\n\nlet a: /*a1*/Bar;\nlet b: /*a2*/Bar<string>;\nlet c: /*a3*/Bar<string, number>;\nlet d: /*a4*/Bar./*b0*/X;\nlet e: /*a5*/Bar./*b1*/X<string>;\nlet f: /*a6*/Bar./*c0*/X./*d0*/Y;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "a0", "a1", "a2", "a3", "a4", "a5", "a6", "b0", "b1", "c0", "d0");
            }
            finally
            {
                done();
            }
        });
    }

}
