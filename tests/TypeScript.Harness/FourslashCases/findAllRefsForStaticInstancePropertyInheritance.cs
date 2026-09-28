// ported from: internal/fourslash/tests/findAllRefsForStaticInstancePropertyInheritance_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsForStaticInstancePropertyInheritanceCase
{
    public static void TestFindAllRefsForStaticInstancePropertyInheritance(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class X{\n\t/*0*/foo:any\n}\n\nclass Y extends X{\n\tstatic /*1*/foo:any\n}\n\nclass Z extends Y{\n\tstatic /*2*/foo:any\n\t/*3*/foo:any\n}\n\nconst x = new X();\nconst y = new Y();\nconst z = new Z();\nx./*4*/foo;\ny./*5*/foo;\nz./*6*/foo;\nY./*7*/foo;\nZ./*8*/foo;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "0", "1", "2", "3", "4", "5", "6", "7", "8");
            }
            finally
            {
                done();
            }
        });
    }

}
