// ported from: internal/fourslash/tests/tsxGoToDefinitionClassInDifferentFile_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class TsxGoToDefinitionClassInDifferentFileCase
{
    public static void TestTsxGoToDefinitionClassInDifferentFile(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @jsx: preserve\n// @Filename: C.tsx\nexport default class /*def*/C {}\n// @Filename: a.tsx\nimport C from \"./C\";\nconst foo = </*use*/C />;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNoErrors(t);
                f.VerifyBaselineGoToDefinition(t, false, "use");
            }
            finally
            {
                done();
            }
        });
    }

}
