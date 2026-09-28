// ported from: internal/fourslash/tests/renameDeclarationKeywords_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameDeclarationKeywordsCase
{
    public static void TestRenameDeclarationKeywords(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("[|{| \"id\": \"baseDecl\" |}class [|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeId\": \"baseDecl\" |}Base|] {}|]\n[|{| \"id\": \"implemented1Decl\" |}interface [|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeId\": \"implemented1Decl\" |}Implemented1|] {}|]\n[|{| \"id\": \"classDecl1\" |}[|class|] [|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeId\": \"classDecl1\" |}C1|] [|extends|] [|Base|] [|implements|] [|Implemented1|] {\n    [|{| \"id\": \"getDecl\" |}[|get|] [|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeId\": \"getDecl\" |}e|]() { return 1; }|]\n    [|{| \"id\": \"setDecl\" |}[|set|] [|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeId\": \"setDecl\" |}e|](v) {}|]\n}|]\n[|{| \"id\": \"interfaceDecl1\" |}[|interface|] [|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeId\": \"interfaceDecl1\" |}I1|] [|extends|] [|Base|] { }|]\n[|{| \"id\": \"typeDecl\" |}[|type|] [|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeId\": \"typeDecl\" |}T|] = { }|]\n[|{| \"id\": \"enumDecl\" |}[|enum|] [|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeId\": \"enumDecl\" |}E|] { }|]\n[|{| \"id\": \"namespaceDecl\" |}[|namespace|] [|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeId\": \"namespaceDecl\" |}N|] { }|]\n[|{| \"id\": \"moduleDecl\" |}[|module|] [|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeId\": \"moduleDecl\" |}M|] { }|]\n[|{| \"id\": \"functionDecl\" |}[|function|] [|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeId\": \"functionDecl\" |}fn|]() {}|]\n[|{| \"id\": \"varDecl\" |}[|var|] [|{| \"isWriteAccess\": false, \"isDefinition\": true, \"contextRangeId\": \"varDecl\" |}x|];|]\n[|{| \"id\": \"letDecl\" |}[|let|] [|{| \"isWriteAccess\": false, \"isDefinition\": true, \"contextRangeId\": \"letDecl\" |}y|];|]\n[|{| \"id\": \"constDecl\" |}[|const|] [|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeId\": \"constDecl\" |}z|] = 1;|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineRename(t, null!, f.Ranges()[5], f.Ranges()[7], f.Ranges()[9], f.Ranges()[12], f.Ranges()[15], f.Ranges()[18], f.Ranges()[20], f.Ranges()[23], f.Ranges()[26], f.Ranges()[29], f.Ranges()[32], f.Ranges()[35], f.Ranges()[38], f.Ranges()[41], f.Ranges()[44]);
            }
            finally
            {
                done();
            }
        });
    }

}
