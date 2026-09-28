// ported from: internal/fourslash/tests/jsDocFunctionSignatures3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsDocFunctionSignatures3Case
{
    public static void TestJsDocFunctionSignatures3(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowNonTsExtensions: true\n// @Filename: Foo.js\nvar someObject = {\n    /**\n     * @param {string} param1 Some string param.\n     * @param {number} parm2  Some number param.\n     */\n    someMethod: function(param1, param2) {\n        console.log(param1/*1*/);\n        return false;\n    },\n    /**\n     * @param {number} p1  Some number param.\n     */\n    otherMethod(p1) {\n        p1/*2*/\n    }\n\n};"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.Insert(t, ".");
                f.VerifyCompletions(t, null!, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "substring", Kind = LSP.CompletionItemKind.Method }] } });
                f.Backspace(t, 1);
                f.GoToMarker(t, "2");
                f.Insert(t, ".");
                f.VerifyCompletions(t, null!, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "toFixed", Kind = LSP.CompletionItemKind.Method }] } });
                f.Backspace(t, 1);
            }
            finally
            {
                done();
            }
        });
    }

}
