// ported from: internal/fourslash/tests/javaScriptModules14_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JavaScriptModules14Case
{
    public static void TestJavaScriptModules14(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: myMod.js\nif (true) {\n    exports.b = true;\n} else {\n    exports.n = 3;\n}\nfunction fn() {\n    exports.s = 'foo';\n}\nvar invisible = true;\n// @Filename: isGlobal.js\nvar y = 10;\n// @Filename: consumer.js\nvar x = require('myMod');\n/**/;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "y", SortText = LS.SortTextGlobalsOrKeywords }], Excludes = ["invisible"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
