// ported from: internal/fourslash/tests/pathCompletionsTypesVersionsWildcard2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class PathCompletionsTypesVersionsWildcard2Case
{
    public static void TestPathCompletionsTypesVersionsWildcard2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: commonjs\n// @resolveJsonModule: false\n// @Filename: /node_modules/foo/package.json\n{\n  \"types\": \"index.d.ts\",\n  \"typesVersions\": {\n    \"<=3.4.1\": {\n      \"*\": [\"ts-old/*\"]\n    }\n  }\n}\n// @Filename: /node_modules/foo/nope.d.ts\nexport const nope = 0;\n// @Filename: /node_modules/foo/ts-old/index.d.ts\nexport const index = 0;\n// @Filename: /node_modules/foo/ts-old/blah.d.ts\nexport const blah = 0;\n// @Filename: /node_modules/foo/ts-old/subfolder/one.d.ts\nexport const one = 0;\n// @Filename: /a.ts\nimport { } from \"foo//**/\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["nope", "ts-old"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
