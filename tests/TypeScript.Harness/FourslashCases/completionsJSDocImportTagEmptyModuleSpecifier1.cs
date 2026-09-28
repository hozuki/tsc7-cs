// ported from: internal/fourslash/tests/completionsJSDocImportTagEmptyModuleSpecifier1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsJSDocImportTagEmptyModuleSpecifier1Case
{
    public static void TestCompletionsJSDocImportTagEmptyModuleSpecifier1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: true\n// @checkJs: true\n// @allowJs: true\n// @moduleResolution: nodenext\n// @filename: node_modules/pkg/index.d.ts\nexport type MyUnion = string | number;\n// @filename: index.js\n/** @import { MyUnion } from \"/**/\" */"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["pkg"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
