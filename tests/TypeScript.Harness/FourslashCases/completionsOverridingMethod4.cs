// ported from: internal/fourslash/tests/completionsOverridingMethod4_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsOverridingMethod4Case
{
    public static void TestCompletionsOverridingMethod4(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @newline: LF\n// @Filename: secret.ts\nclass Secret {\n    #secret(): string {\n        return \"secret\";\n    }\n\n    private tell(): string {\n        return this.#secret();\n    }\n\n    protected hint(): string {\n        return \"hint\";\n    }\n\n    public refuse(): string {\n        return \"no comments\";\n    }\n}\n\nclass Gossip extends Secret {\n    /* no telling secrets */\n    /*a*/\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "a", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "hint", InsertText = "protected hint(): string {\n}", FilterText = "hint", SortText = LS.SortTextLocationPriority }, new LSP.CompletionItem { Label = "refuse", InsertText = "public refuse(): string {\n}", FilterText = "refuse", SortText = LS.SortTextLocationPriority }], Excludes = ["tell", "#secret"] }, UserPreferences = new LSUP.UserPreferences { IncludeCompletionsWithClassMemberSnippets = TRI.TSTrue } });
            }
            finally
            {
                done();
            }
        });
    }

}
