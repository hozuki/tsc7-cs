// ported from: internal/fourslash/tests/autoImportsNodeNext1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportsNodeNext1Case
{
    public static void TestAutoImportsNodeNext1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: node18\n// @Filename: /node_modules/pack/package.json\n{\n    \"name\": \"pack\",\n    \"version\": \"1.0.0\",\n    \"exports\": {\n        \".\": \"./main.mjs\"\n    }\n}\n// @Filename: /node_modules/pack/main.d.mts\nimport {} from \"./unreachable.mjs\";\nexport const fromMain = 0;\n// @Filename: /node_modules/pack/unreachable.d.mts\nexport const fromUnreachable = 0;\n// @Filename: /index.mts\nimport { fromMain } from \"pack\";\nfromUnreachable/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifyImportFixAtPosition(t, [], null!);
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Excludes = ["fromUnreachable"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
