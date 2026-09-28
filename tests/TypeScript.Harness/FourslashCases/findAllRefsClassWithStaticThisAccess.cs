// ported from: internal/fourslash/tests/findAllRefsClassWithStaticThisAccess_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsClassWithStaticThisAccessCase
{
    public static void TestFindAllRefsClassWithStaticThisAccess(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("[|class /*0*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 0 |}C|] {\n    static s() {\n        /*1*/[|this|];\n    }\n    static get f() {\n        return /*2*/[|this|];\n\n        function inner() { this; }\n        class Inner { x = this; }\n    }\n}|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "0", "1", "2");
                f.VerifyBaselineRename(t, null!, f.Ranges()[1]);
            }
            finally
            {
                done();
            }
        });
    }

}
