// ported from: internal/fourslash/tests/completionListAtInvalidLocations_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListAtInvalidLocationsCase
{
    public static void TestCompletionListAtInvalidLocations(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("var v1 = '';\n\" /*openString1*/\nvar v2 = '';\n\"/*openString2*/\nvar v3 = '';\n\" bar./*openString3*/\nvar v4 = '';\n// bar./*inComment1*/\nvar v6 = '';\n// /*inComment2*/\nvar v7 = '';\n/* /*inComment3*/\nvar v11 = '';\n  // /*inComment4*/\nvar v12 = '';\ntype htm/*inTypeAlias*/\n\n//  /*inComment5*/\nfoo;\nvar v10 = /reg/*inRegExp1*/ex/;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["openString1", "openString2", "openString3"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["inComment1", "inComment2", "inComment3", "inComment4", "inTypeAlias", "inComment5", "inRegExp1"] }, null!);
            }
            finally
            {
                done();
            }
        });
    }

}
