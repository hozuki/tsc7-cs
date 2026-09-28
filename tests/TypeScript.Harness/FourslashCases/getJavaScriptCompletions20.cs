// ported from: internal/fourslash/tests/getJavaScriptCompletions20_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetJavaScriptCompletions20Case
{
    public static void TestGetJavaScriptCompletions20(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n// @allowNonTsExtensions: true\n// @Filename: file.js\n/**\n * A person\n * @constructor\n * @param {string} name - The name of the person.\n * @param {number} age - The age of the person.\n */\nfunction Person(name, age) {\n    this.name = name;\n    this.age = age;\n}\n\n\nPerson.getName = 10;\nPerson.getNa/**/ = 10;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = U.CompletionFunctionMembersWithPrototypePlus(["getName", "getNa", new LSP.CompletionItem { Label = "Person", SortText = LS.SortTextJavascriptIdentifiers }, new LSP.CompletionItem { Label = "name", SortText = LS.SortTextJavascriptIdentifiers }, new LSP.CompletionItem { Label = "age", SortText = LS.SortTextJavascriptIdentifiers }]) } });
            }
            finally
            {
                done();
            }
        });
    }

}
