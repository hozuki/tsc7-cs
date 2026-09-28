// ported from: internal/fourslash/tests/referencesForTypeKeywords_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ReferencesForTypeKeywordsCase
{
    public static void TestReferencesForTypeKeywords(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface I {}\nfunction f<T /*typeParam_extendsKeyword*/extends I>() {}\ntype A1<T, U> = T /*conditionalType_extendsKeyword*/extends U ? 1 : 0;\ntype A2<T> = T extends /*inferType_inferKeyword*/infer U ? 1 : 0;\ntype A3<T> = { [P /*mappedType_inOperator*/in keyof T]: 1 };\ntype A4<T> = /*keyofOperator_keyofKeyword*/keyof T;\ntype A5<T> = /*readonlyOperator_readonlyKeyword*/readonly T[];"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "typeParam_extendsKeyword", "conditionalType_extendsKeyword", "inferType_inferKeyword", "mappedType_inOperator", "keyofOperator_keyofKeyword", "readonlyOperator_readonlyKeyword");
            }
            finally
            {
                done();
            }
        });
    }

}
