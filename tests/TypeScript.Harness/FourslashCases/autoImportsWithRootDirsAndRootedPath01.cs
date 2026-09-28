// ported from: internal/fourslash/tests/autoImportsWithRootDirsAndRootedPath01_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportsWithRootDirsAndRootedPath01Case
{
    public static void TestAutoImportsWithRootDirsAndRootedPath01(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /dir/foo.ts\n export function foo() {}\n// @Filename: /dir/bar.ts\n /*$*/\n// @Filename: /dir/tsconfig.json\n{\n    \"compilerOptions\": {\n        \"module\": \"commonjs\",\n        \"moduleResolution\": \"classic\",\n        \"rootDirs\": [\"D:/\"]\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "$");
                f.VerifyCompletions(t, null!, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems {  } });
            }
            finally
            {
                done();
            }
        });
    }

}
