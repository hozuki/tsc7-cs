// ported from: internal/fourslash/tests/completionsImport_exportEquals_global_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsImport_exportEquals_globalCase
{
    public static void TestCompletionsImport_exportEquals_global(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n// @module: es6\n// @Filename: /console.d.ts\n interface Console {}\n declare var console: Console;\n declare module \"console\" {\n   export = console;\n }\n// @Filename: /react-native.d.ts\n import 'console';\n declare global {\n   interface Console {}\n   var console: Console;\n }\n// @Filename: /a.ts\nconso/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = U.CompletionGlobalsPlus([new LSP.CompletionItem { Label = "console", SortText = LS.SortTextGlobalsOrKeywords }], false) } });
            }
            finally
            {
                done();
            }
        });
    }

}
