// ported from: internal/fourslash/tests/completionsAfterKeywordsInBlock_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsAfterKeywordsInBlockCase
{
    public static void TestCompletionsAfterKeywordsInBlock(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class C1 {\n    method(map: Map<string, string>, key: string, defaultValue: string) {\n        try {\n            return map.get(key)!;\n        }\n        catch {\n            return default/*1*/\n        }\n    }\n}\nclass C2 {\n    method(map: Map<string, string>, key: string, defaultValue: string) {\n        if (map.has(key)) {\n            return map.get(key)!;\n        }\n        else {\n            return default/*2*/\n        }\n    }\n}\nclass C3 {\n    method(map: Map<string, string>, key: string, returnValue: string) {\n        try {\n            return map.get(key)!;\n        }\n        catch {\n            return return/*3*/\n        }\n    }\n}\nclass C4 {\n    method(map: Map<string, string>, key: string, returnValue: string) {\n        if (map.has(key)) {\n            return map.get(key)!;\n        }\n        else {\n            return return/*4*/\n        }\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["1", "2"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "defaultValue", SortText = LS.SortTextLocationPriority }] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["3", "4"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "returnValue", SortText = LS.SortTextLocationPriority }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
