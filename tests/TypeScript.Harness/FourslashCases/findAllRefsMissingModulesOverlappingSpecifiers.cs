// ported from: internal/fourslash/tests/findAllRefsMissingModulesOverlappingSpecifiers_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsMissingModulesOverlappingSpecifiersCase
{
    public static void TestFindAllRefsMissingModulesOverlappingSpecifiers(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// https://github.com/microsoft/TypeScript/issues/5551\nimport { resolve/*0*/ as resolveUrl } from \"idontcare\";\nimport { resolve/*1*/ } from \"whatever\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "0", "1");
            }
            finally
            {
                done();
            }
        });
    }

}
