// ported from: internal/fourslash/tests/qualifiedName_import-declaration-with-variable-entity-names_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QualifiedName_import_declaration_with_variable_entity_namesCase
{
    public static void TestQualifiedName_import_declaration_with_variable_entity_names(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("namespace Alpha {\n    export var [|{| \"name\" : \"def\" |}x|] = 100;\n}\n\nnamespace Beta {\n    import p = Alpha.[|{| \"name\" : \"import\" |}x|];\n}\n\nvar x = Alpha.[|{| \"name\" : \"mem\" |}x|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "import");
                f.VerifyCompletions(t, null!, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "x", Detail = "var Alpha.x: number" }] } });
                f.VerifyBaselineDocumentHighlights(t, null!, "import");
                f.VerifyBaselineGoToDefinition(t, false, "import");
            }
            finally
            {
                done();
            }
        });
    }

}
