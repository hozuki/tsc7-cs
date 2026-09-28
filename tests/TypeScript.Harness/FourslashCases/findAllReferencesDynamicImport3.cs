// ported from: internal/fourslash/tests/findAllReferencesDynamicImport3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllReferencesDynamicImport3Case
{
    public static void TestFindAllReferencesDynamicImport3(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: foo.ts\n[|export function /*0*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 0 |}bar|]() { return \"bar\"; }|]\nimport('./foo').then(([|{ /*1*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 2 |}bar|] }|]) => undefined);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "0", "1");
                f.VerifyBaselineRename(t, null!, f.Ranges()[1], f.Ranges()[3]);
            }
            finally
            {
                done();
            }
        });
    }

}
