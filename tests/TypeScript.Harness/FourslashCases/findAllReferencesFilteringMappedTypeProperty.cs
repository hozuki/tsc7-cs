// ported from: internal/fourslash/tests/findAllReferencesFilteringMappedTypeProperty_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllReferencesFilteringMappedTypePropertyCase
{
    public static void TestFindAllReferencesFilteringMappedTypeProperty(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("const obj = { /*1*/a: 1, b: 2 };\nconst filtered: { [P in keyof typeof obj as P extends 'b' ? never : P]: 0; } = { /*2*/a: 0 };\nfiltered./*3*/a;"u8);
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
