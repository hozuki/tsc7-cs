// ported from: internal/fourslash/tests/findAllRefsWithLeadingUnderscoreNames6_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsWithLeadingUnderscoreNames6Case
{
    public static void TestFindAllRefsWithLeadingUnderscoreNames6(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class Foo {\n    public _bar;\n    /*1*/public /*2*/__bar;\n    public ___bar;\n    public ____bar;\n}\n\nvar x: Foo;\nx._bar;\nx./*3*/__bar;\nx.___bar;\nx.____bar;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1", "2", "3");
            }
            finally
            {
                done();
            }
        });
    }

}
