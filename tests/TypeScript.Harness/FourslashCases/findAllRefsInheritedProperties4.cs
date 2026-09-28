// ported from: internal/fourslash/tests/findAllRefsInheritedProperties4_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsInheritedProperties4Case
{
    public static void TestFindAllRefsInheritedProperties4(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface C extends D {\n    /*0*/prop0: string;\n    /*1*/prop1: number;\n}\n\ninterface D extends C {\n    /*2*/prop0: string;\n}\n\nvar d: D;\nd./*3*/prop0;\nd./*4*/prop1;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "0", "2", "3", "1", "4");
            }
            finally
            {
                done();
            }
        });
    }

}
