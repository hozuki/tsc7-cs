// ported from: internal/fourslash/tests/completionEntryForClassMembers_StaticWhenBaseTypeIsNotResolved_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionEntryForClassMembers_StaticWhenBaseTypeIsNotResolvedCase
{
    public static void TestCompletionEntryForClassMembers_StaticWhenBaseTypeIsNotResolved(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /a.ts\nimport React from 'react'\nclass Slider extends React.Component {\n    static defau/**/ltProps = {\n        onMouseDown: () => { },\n        onMouseUp: () => { },\n        unit: 'px',\n    }\n    handleChange = () => 10;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = U.CompletionClassElementKeywords } });
            }
            finally
            {
                done();
            }
        });
    }

}
