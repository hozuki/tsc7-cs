// ported from: internal/fourslash/tests/doubleUnderscoreCompletions_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class DoubleUnderscoreCompletionsCase
{
    public static void TestDoubleUnderscoreCompletions(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: a.js\nfunction MyObject(){\n    this.__property = 1;\n}\nvar instance = new MyObject();\ninstance./*1*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "__property", Detail = "(property) MyObject.__property: number" }, new LSP.CompletionItem { Label = "instance", SortText = LS.SortTextJavascriptIdentifiers }, new LSP.CompletionItem { Label = "MyObject", SortText = LS.SortTextJavascriptIdentifiers }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
