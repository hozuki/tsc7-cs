// ported from: internal/fourslash/tests/importAttributesTypesLanguageService_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportAttributesTypesLanguageServiceCase
{
    public static void TestCompletionsPatternAmbientModuleWithImportAttributes(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /tsconfig.json\n{ \"compilerOptions\": { \"module\": \"preserve\", \"moduleResolution\": \"bundler\" } }\n\n// @Filename: /types.d.ts\ndeclare const outerAttributeName: unique symbol;\ntype OuterAttributeValue = \"css\";\n\ndeclare module \"*.asset\" with { /*attributeName*/type: /*attributeValue*/\"css\" } {\n\texport interface CssAttributeValue {}\n    export const shared: \"css\";\n    export const cssOnly: \"css-only\";\n}\ndeclare module \"*.asset\" with { type: \"text\" } {\n\texport interface TextAttributeValue {}\n    export const shared: \"text\";\n    export const textOnly: \"text-only\";\n}\n\n// @Filename: /index.ts\nimport * as css from \"./style.asset\" with { type: \"css\" };\nimport * as text from \"./copy.asset\" with { type: \"text\" };\ncss./*css*/cssOnly;\ntext./*text*/textOnly;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "attributeName", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["outerAttributeName"], Excludes = ["CssAttributeValue", "cssOnly", "shared"] } });
                f.VerifyCompletions(t, "attributeValue", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["OuterAttributeValue"], Excludes = ["CssAttributeValue", "TextAttributeValue"] } });
                f.VerifyCompletions(t, "css", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["cssOnly", "shared"], Excludes = ["textOnly"] } });
                f.VerifyCompletions(t, "text", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["shared", "textOnly"], Excludes = ["cssOnly"] } });
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestPatternAmbientModuleWithImportAttributesLanguageService(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /tsconfig.json\n{ \"compilerOptions\": { \"module\": \"preserve\", \"moduleResolution\": \"bundler\" } }\n\n// @Filename: /types.d.ts\ndeclare module \"*.asset\" with { type: \"css\" } {\n    export interface CssPayload { kind: \"css\" }\n    export const shared: CssPayload;\n}\ndeclare module \"*.asset\" with { type: \"text\" } {\n    export interface TextPayload { kind: \"text\" }\n    export const shared: TextPayload;\n}\n\n// @Filename: /index.ts\nimport * as css from /*cssModule*/\"./style.asset\" with { type: \"css\" };\nimport * as text from /*textModule*/\"./copy.asset\" with { type: \"text\" };\ncss./*cssUse*/shared;\ntext./*textUse*/shared;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNoErrors(t);
                f.VerifyBaselineHover(t);
                f.VerifyBaselineGoToDefinition(t, true, "cssModule", "textModule", "cssUse", "textUse");
                f.VerifyBaselineGoToTypeDefinition(t, "cssUse", "textUse");
                f.VerifyBaselineFindAllReferences(t, "cssUse", "textUse");
                f.VerifyBaselineRename(t, null!, "cssUse", "textUse");
                f.VerifyBaselineDocumentHighlights(t, null!, "cssUse", "textUse");
            }
            finally
            {
                done();
            }
        });
    }

}
