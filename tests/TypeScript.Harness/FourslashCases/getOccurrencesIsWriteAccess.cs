// ported from: internal/fourslash/tests/getOccurrencesIsWriteAccess_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetOccurrencesIsWriteAccessCase
{
    public static void TestGetOccurrencesIsWriteAccess(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("var [|{| \"isWriteAccess\": true |}x|] = 0;\nvar assignmentRightHandSide = [|{| \"isWriteAccess\": false |}x|];\nvar assignmentRightHandSide2 = 1 + [|{| \"isWriteAccess\": false |}x|];\n\n[|{| \"isWriteAccess\": true |}x|] = 1;\n[|{| \"isWriteAccess\": true |}x|] = [|{| \"isWriteAccess\": false |}x|] + [|{| \"isWriteAccess\": false |}x|];\n\n[|{| \"isWriteAccess\": false |}x|] == 1;\n[|{| \"isWriteAccess\": false |}x|] <= 1;\n\nvar preIncrement = ++[|{| \"isWriteAccess\": true |}x|];\nvar postIncrement = [|{| \"isWriteAccess\": true |}x|]++;\nvar preDecrement = --[|{| \"isWriteAccess\": true |}x|];\nvar postDecrement = [|{| \"isWriteAccess\": true |}x|]--;\n\n[|{| \"isWriteAccess\": true |}x|] += 1;\n[|{| \"isWriteAccess\": true |}x|] <<= 1;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineDocumentHighlights(t, null!, f.Ranges()[0]);
            }
            finally
            {
                done();
            }
        });
    }

}
