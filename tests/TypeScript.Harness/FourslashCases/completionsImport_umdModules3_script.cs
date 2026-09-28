// ported from: internal/fourslash/tests/completionsImport_umdModules3_script_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsImport_umdModules3_scriptCase
{
    public static void TestCompletionsImport_umdModules3_script(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @filename: /package.json\n{ \"dependencies\": { \"@types/classnames\": \"*\" } }\n// @filename: /tsconfig.json\n{ \"compilerOptions\": { \"module\": \"es2015\", \"types\": [\"*\"] }}\n// @filename: /node_modules/@types/classnames/package.json\n{ \"name\": \"@types/classnames\", \"types\": \"index.d.ts\" }\n// @filename: /node_modules/@types/classnames/index.d.ts\ndeclare const classNames: () => string;\nexport = classNames;\nexport as namespace classNames;\n// @filename: /SomeReactComponent.tsx\n\nconst el1 = <div className={class/*1*/}>foo</div>"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.VerifyCompletions(t, null!, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "classNames", SortText = LS.SortTextGlobalsOrKeywords }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
