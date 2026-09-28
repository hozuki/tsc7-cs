// ported from: internal/fourslash/tests/basicJSDocCompletions_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class BasicJSDocCompletionsCase
{
    public static void TestBasicJSDocCompletions(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\n// @filename: file.js\n// @allowJs: true\n/**\n * @/*1*/\n */\nfunction foo(x) {\n  return x + 1;\n}\n  \n/**\n * /*2*/\n */\nfunction bar(x, { y }) {\n  return x + y;\n}\n\n/**\n * @param {number} x\n * /*3*/\n */\nfunction baz(x, { y }) {\n  return x + y;\n}\n\n/**\n * @param {number} x\n * @param {object} param1 \n * @param {n/*4*/} param1.y \n */\nfunction baz(x, { y }) {\n  return x + y;\n}\n\n/**\n * @/*5*/\n */\nfunction baz(x = 0) {\n  return x * 2;\n}\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "link", Kind = LSP.CompletionItemKind.Keyword, Detail = "link" }, "param", "returns", "param {*} x "] } });
                f.VerifyCompletions(t, "2", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters }, Items = new F.CompletionsExpectedItems { Includes = ["@param", "@param {*} x "] } });
                f.VerifyCompletions(t, "3", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters }, Items = new F.CompletionsExpectedItems { Includes = ["@param", "@param {object} param1 \n* @param {*} param1.y "] } });
                f.VerifyCompletions(t, "4", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["number"] } });
                f.VerifyCompletions(t, "5", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["param {number} [x=0] "] } });
            }
            finally
            {
                done();
            }
        });
    }

}
