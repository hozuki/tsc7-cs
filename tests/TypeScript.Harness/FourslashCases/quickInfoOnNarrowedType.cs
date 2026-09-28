// ported from: internal/fourslash/tests/quickInfoOnNarrowedType_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoOnNarrowedTypeCase
{
    public static void TestQuickInfoOnNarrowedType(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strictNullChecks: true\nfunction foo(strOrNum: string | number) {\n    if (typeof /*1*/strOrNum === \"number\") {\n        return /*2*/strOrNum;\n    }\n    else {\n        return /*3*/strOrNum.length;\n    }\n}\nfunction bar() {\n   let s: string | undefined;\n   /*4*/s;\n   /*5*/s = \"abc\";\n   /*6*/s;\n}\nclass Foo {\n    #privateProperty: string[] | null;\n    constructor() {\n        this.#privateProperty = null;\n    }\n    testMethod() {\n        if (this.#privateProperty === null)\n            return;\n        this./*7*/#privateProperty;\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(parameter) strOrNum: string | number", "");
                f.VerifyQuickInfoAt(t, "2", "(parameter) strOrNum: number", "");
                f.VerifyQuickInfoAt(t, "3", "(parameter) strOrNum: string", "");
                f.VerifyQuickInfoAt(t, "4", "let s: string | undefined", "");
                f.VerifyQuickInfoAt(t, "5", "let s: string | undefined", "");
                f.VerifyQuickInfoAt(t, "6", "let s: string", "");
                f.VerifyQuickInfoAt(t, "7", "(property) Foo.#privateProperty: string[]", "");
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "strOrNum", Detail = "(parameter) strOrNum: string | number" }] } });
                f.VerifyCompletions(t, "2", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "strOrNum", Detail = "(parameter) strOrNum: number" }] } });
                f.VerifyCompletions(t, "3", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "strOrNum", Detail = "(parameter) strOrNum: string" }] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["4", "5"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "s", Detail = "let s: string | undefined" }] } });
                f.VerifyCompletions(t, "6", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "s", Detail = "let s: string" }] } });
                f.VerifyCompletions(t, "7", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "#privateProperty", Detail = "(property) Foo.#privateProperty: string[]" }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
