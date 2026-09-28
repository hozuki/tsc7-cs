// ported from: internal/fourslash/tests/completionsClassMemberImportTypeNodeParameter1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsClassMemberImportTypeNodeParameter1Case
{
    public static void TestCompletionsClassMemberImportTypeNodeParameter1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: node18\n// @Filename: /generation.d.ts\nexport type GenerationConfigType = { max_length?: number };\n// @FileName: /index.d.ts\nexport declare class PreTrainedModel {\n  _get_generation_config(\n    param: import(\"./generation.js\").GenerationConfigType,\n  ): import(\"./generation.js\").GenerationConfigType;\n}\n\nexport declare class BlenderbotSmallPreTrainedModel extends PreTrainedModel {\n  /*1*/\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "_get_generation_config", InsertText = "_get_generation_config(param: import(\"./generation.js\").GenerationConfigType): import(\"./generation.js\").GenerationConfigType;", FilterText = "_get_generation_config" }] }, UserPreferences = new LSUP.UserPreferences { IncludeCompletionsWithClassMemberSnippets = TRI.TSTrue } });
            }
            finally
            {
                done();
            }
        });
    }

}
