// ported from: internal/fourslash/tests/indirectClassInstantiation_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class IndirectClassInstantiationCase
{
    public static void TestIndirectClassInstantiation(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: something.js\nfunction TestObj(){\n    this.property = \"value\";\n}\nvar constructor = TestObj;\nvar instance = new constructor();\ninstance./*a*/\nvar class2 = function() { };\nclass2.prototype.blah = function() { };\nvar inst2 = new class2();\ninst2.blah/*b*/;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "a");
                f.VerifyCompletions(t, null!, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["property", new LSP.CompletionItem { Label = "blah", SortText = LS.SortTextJavascriptIdentifiers }, new LSP.CompletionItem { Label = "class2", SortText = LS.SortTextJavascriptIdentifiers }, new LSP.CompletionItem { Label = "constructor", SortText = LS.SortTextJavascriptIdentifiers }, new LSP.CompletionItem { Label = "inst2", SortText = LS.SortTextJavascriptIdentifiers }, new LSP.CompletionItem { Label = "instance", SortText = LS.SortTextJavascriptIdentifiers }, new LSP.CompletionItem { Label = "prototype", SortText = LS.SortTextJavascriptIdentifiers }, new LSP.CompletionItem { Label = "TestObj", SortText = LS.SortTextJavascriptIdentifiers }] } });
                f.Backspace(t, 1);
                f.GoToMarker(t, "b");
                f.VerifyQuickInfoIs(t, "(method) class2.blah(): void", "");
            }
            finally
            {
                done();
            }
        });
    }

}
