// ported from: internal/fourslash/tests/autoImportCompletionsForArbitraryNonIdentifierExports_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportCompletionsForArbitraryNonIdentifierExportsCase
{
    public static void TestAutoImportCompletionsForArbitraryNonIdentifierExports(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\n// @module: esnext\n// @Filename: /a.ts\nconst foo = 0;\nexport { foo as \"foo-bar\" };\nexport const fooBar = 1;\n\n// @Filename: /b.ts\nfoo/**/\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { Items = new F.CompletionsExpectedItems { Excludes = ["foo-bar"], Includes = ["fooBar"] }, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value } });
            }
            finally
            {
                done();
            }
        });
    }

}
