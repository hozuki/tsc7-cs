// ported from: internal/fourslash/tests/goToDefinition_filteringMappedType_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinition_filteringMappedTypeCase
{
    public static void TestGoToDefinition_filteringMappedType(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("const obj = { /*def*/a: 1, b: 2 };\nconst filtered: { [P in keyof typeof obj as P extends 'b' ? never : P]: 0; } = { a: 0 };\nfiltered.[|/*ref*/a|];"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "ref");
            }
            finally
            {
                done();
            }
        });
    }

}
