// ported from: internal/fourslash/tests/quickInfoOnNarrowedTypeInModule_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoOnNarrowedTypeInModuleCase
{
    public static void TestQuickInfoOnNarrowedTypeInModule(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: false\nvar strOrNum: string | number;\nnamespace m {\n    var nonExportedStrOrNum: string | number;\n    export var exportedStrOrNum: string | number;\n    var num: number;\n    var str: string;\n    if (typeof /*1*/nonExportedStrOrNum === \"number\") {\n        num = /*2*/nonExportedStrOrNum;\n    }\n    else {\n        str = /*3*/nonExportedStrOrNum.length;\n    }\n    if (typeof /*4*/exportedStrOrNum === \"number\") {\n        strOrNum = /*5*/exportedStrOrNum;\n    }\n    else {\n        strOrNum = /*6*/exportedStrOrNum;\n    }\n}\nif (typeof m./*7*/exportedStrOrNum === \"number\") {\n    strOrNum = m./*8*/exportedStrOrNum;\n}\nelse {\n    strOrNum = m./*9*/exportedStrOrNum;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "var nonExportedStrOrNum: string | number", "");
                f.VerifyQuickInfoAt(t, "2", "var nonExportedStrOrNum: number", "");
                f.VerifyQuickInfoAt(t, "3", "var nonExportedStrOrNum: string", "");
                f.VerifyQuickInfoAt(t, "4", "var m.exportedStrOrNum: string | number", "");
                f.VerifyQuickInfoAt(t, "5", "var m.exportedStrOrNum: number", "");
                f.VerifyQuickInfoAt(t, "6", "var m.exportedStrOrNum: string", "");
                f.VerifyQuickInfoAt(t, "7", "var m.exportedStrOrNum: string | number", "");
                f.VerifyQuickInfoAt(t, "8", "var m.exportedStrOrNum: number", "");
                f.VerifyQuickInfoAt(t, "9", "var m.exportedStrOrNum: string", "");
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "nonExportedStrOrNum", Detail = "var nonExportedStrOrNum: string | number" }] } });
                f.VerifyCompletions(t, "2", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "nonExportedStrOrNum", Detail = "var nonExportedStrOrNum: number" }] } });
                f.VerifyCompletions(t, "3", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "nonExportedStrOrNum", Detail = "var nonExportedStrOrNum: string" }] } });
                f.VerifyCompletions(t, "4", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "exportedStrOrNum", Detail = "var exportedStrOrNum: string | number" }] } });
                f.VerifyCompletions(t, "5", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "exportedStrOrNum", Detail = "var exportedStrOrNum: number" }] } });
                f.VerifyCompletions(t, "6", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "exportedStrOrNum", Detail = "var exportedStrOrNum: string" }] } });
                f.VerifyCompletions(t, "7", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "exportedStrOrNum", Detail = "var m.exportedStrOrNum: string | number" }] } });
                f.VerifyCompletions(t, "8", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "exportedStrOrNum", Detail = "var m.exportedStrOrNum: number" }] } });
                f.VerifyCompletions(t, "9", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "exportedStrOrNum", Detail = "var m.exportedStrOrNum: string" }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
