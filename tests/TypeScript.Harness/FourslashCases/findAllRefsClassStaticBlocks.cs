// ported from: internal/fourslash/tests/findAllRefsClassStaticBlocks_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsClassStaticBlocksCase
{
    public static void TestFindAllRefsClassStaticBlocks(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class ClassStaticBocks {\n    static x;\n    [|[|/*classStaticBocks1*/static|] {}|]\n    static y;\n    [|[|/*classStaticBocks2*/static|] {}|]\n    static y;\n    [|[|/*classStaticBocks3*/static|] {}|]\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "classStaticBocks1", "classStaticBocks2", "classStaticBocks3");
            }
            finally
            {
                done();
            }
        });
    }

}
