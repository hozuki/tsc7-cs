// ported from: internal/fourslash/tests/nodeModulesFileEditStillAllowsResolutionsToWork_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class NodeModulesFileEditStillAllowsResolutionsToWorkCase
{
    public static void TestNodeModulesFileEditStillAllowsResolutionsToWork(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /tsconfig.json\n{ \"compilerOptions\": { \"module\": \"nodenext\", \"strict\": true } }\n// @Filename: /package.json\n{ \"type\": \"module\", \"imports\": { \"#foo\": \"./foo.cjs\" } }\n// @Filename: /foo.cts\nexport const x = 1;\n// @Filename: /index.ts\nimport * as mod from \"#foo\";\n/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.Insert(t, "mod.x");
                f.VerifyNoErrors(t);
                f.VerifySuggestionDiagnostics(t, null!);
            }
            finally
            {
                done();
            }
        });
    }

}
