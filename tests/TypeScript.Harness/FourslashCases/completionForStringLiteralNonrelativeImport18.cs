// ported from: internal/fourslash/tests/completionForStringLiteralNonrelativeImport18_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionForStringLiteralNonrelativeImport18Case
{
    public static void TestCompletionForStringLiteralNonrelativeImport18(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: tsconfig.json\n{\n    \"compilerOptions\": {\n       \"paths\": {\n           \"/*\": [\"./*\"]\n       },\n    }\n}\n// @Filename: test0.ts\nimport * as foo1 from \"/path/w/*first*/\n// @Filename: path/whatever.ts\nexport {}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["first"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["whatever"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
