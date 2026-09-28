// ported from: internal/fourslash/tests/renameParameterPropertyDeclaration2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameParameterPropertyDeclaration2Case
{
    public static void TestRenameParameterPropertyDeclaration2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class Foo {\n    constructor([|public [|{| \"contextRangeIndex\": 0 |}publicParam|]: number|]) {\n        let publicParam = [|publicParam|];\n        this.[|publicParam|] += 10;\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineRenameAtRangesWithText(t, null!, "publicParam");
            }
            finally
            {
                done();
            }
        });
    }

}
