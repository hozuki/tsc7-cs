// ported from: internal/fourslash/tests/renameInConfiguredProject_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameInConfiguredProjectCase
{
    public static void TestRenameInConfiguredProject(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: referencesForGlobals_1.ts\n[|var [|{| \"contextRangeIndex\": 0 |}globalName|] = 0;|]\n// @Filename: referencesForGlobals_2.ts\nvar y = [|globalName|];\n// @Filename: tsconfig.json\n{ \"files\": [\"referencesForGlobals_1.ts\", \"referencesForGlobals_2.ts\"], \"compilerOptions\": { \"lib\": [\"es5\"] } }"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.VerifyBaselineRename(t, null!, U.ToAny(f.Ranges().Skip(1)));
            }
            finally
            {
                done();
            }
        });
    }

}
