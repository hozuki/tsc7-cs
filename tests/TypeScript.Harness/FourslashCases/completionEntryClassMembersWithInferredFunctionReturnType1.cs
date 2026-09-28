// ported from: internal/fourslash/tests/completionEntryClassMembersWithInferredFunctionReturnType1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionEntryClassMembersWithInferredFunctionReturnType1Case
{
    public static void TestCompletionEntryClassMembersWithInferredFunctionReturnType1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @filename: /tokenizer.ts\nexport default abstract class Tokenizer {\n  errorBuilder() {\n    return (pos: number, lineStart: number, curLine: number) => {};\n  }\n}\n// @filename: /expression.ts\nimport Tokenizer from \"./tokenizer.js\";\n\nexport default abstract class ExpressionParser extends Tokenizer {\n  /**/\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "errorBuilder", InsertText = "errorBuilder(): (pos: number, lineStart: number, curLine: number) => void {\n}", FilterText = "errorBuilder" }] }, UserPreferences = new LSUP.UserPreferences { IncludeCompletionsWithClassMemberSnippets = TRI.TSTrue } });
            }
            finally
            {
                done();
            }
        });
    }

}
