// ported from: internal/fourslash/tests/findAllRefsPrefixSuffixPreference_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsPrefixSuffixPreferenceCase
{
    public static void TestFindAllRefsPrefixSuffixPreference(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /file1.ts\ndeclare function log(s: string | number): void;\n[|const /*q0*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 0 |}q|] = 1;|]\n[|export { /*q1*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 2 |}q|] };|]\nconst x = {\n    [|/*z0*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 4 |}z|]: 'value'|]\n}\n[|const { /*z1*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 6 |}z|] } = x;|]\nlog(/*z2*/[|z|]);\n// @Filename: /file2.ts\ndeclare function log(s: string | number): void;\n[|import { /*q2*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 9 |}q|] } from \"./file1\";|]\nlog(/*q3*/[|q|] + 1);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNoErrors(t);
                f.VerifyBaselineFindAllReferences(t, "q0", "q1", "q2", "q3", "z0", "z1", "z2");
                f.VerifyBaselineRename(t, new LSUP.UserPreferences { UseAliasesForRename = TRI.TSTrue }, f.Ranges()[1], f.Ranges()[3], f.Ranges()[10], f.Ranges()[11]);
                f.VerifyBaselineRename(t, new LSUP.UserPreferences { UseAliasesForRename = TRI.TSFalse }, f.Ranges()[1], f.Ranges()[3], f.Ranges()[10], f.Ranges()[11]);
                f.VerifyBaselineRename(t, new LSUP.UserPreferences { UseAliasesForRename = TRI.TSTrue }, f.Ranges()[5], f.Ranges()[7], f.Ranges()[8]);
                f.VerifyBaselineRename(t, new LSUP.UserPreferences { UseAliasesForRename = TRI.TSFalse }, f.Ranges()[5], f.Ranges()[7], f.Ranges()[8]);
            }
            finally
            {
                done();
            }
        });
    }

}
