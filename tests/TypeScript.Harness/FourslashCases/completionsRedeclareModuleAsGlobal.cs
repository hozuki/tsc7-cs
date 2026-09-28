// ported from: internal/fourslash/tests/completionsRedeclareModuleAsGlobal_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsRedeclareModuleAsGlobalCase
{
    public static void TestCompletionsRedeclareModuleAsGlobal(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @esModuleInterop: true,\n// @target: esnext\n// @Filename: /myAssert.d.ts\ndeclare function assert(value:any, message?:string):void;\nexport = assert;\nexport as namespace assert;\n// @Filename: /ambient.d.ts\nimport assert from './myAssert';\n\ntype Assert = typeof assert;\n\ndeclare global {\n  const assert: Assert;\n}\n// @Filename: /index.ts\n/// <reference path=\"./ambient.d.ts\" />\nasser/**/;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "assert", SortText = LS.SortTextGlobalsOrKeywords }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
