// ported from: internal/fourslash/tests/getOccurrencesLoopBreakContinue_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetOccurrencesLoopBreakContinueCase
{
    public static void TestGetOccurrencesLoopBreakContinue(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("var arr = [1, 2, 3, 4];\nlabel1: [|for|] (var n in arr) {\n    [|break|];\n    [|continue|];\n    [|br/**/eak|] label1;\n    [|continue|] label1;\n\n    label2: for (var i = 0; i < arr[n]; i++) {\n        [|break|] label1;\n        [|continue|] label1;\n\n        break;\n        continue;\n        break label2;\n        continue label2;\n\n        function foo() {\n            label3: while (true) {\n                break;\n                continue;\n                break label3;\n                continue label3;\n\n                // these cross function boundaries\n                break label1;\n                continue label1;\n                break label2;\n                continue label2;\n\n                label4: do {\n                    break;\n                    continue;\n                    break label4;\n                    continue label4;\n\n                    break label3;\n                    continue label3;\n\n                    switch (10) {\n                        case 1:\n                        case 2:\n                            break;\n                            break label4;\n                        default:\n                            continue;\n                    }\n\n                    // these cross function boundaries\n                    break label1;\n                    continue label1;\n                    break label2;\n                    continue label2;\n                    () => { break; }\n                } while (true)\n            }\n        }\n    }\n}\n\nlabel5: while (true) break label5;\n\nlabel7: while (true) continue label5;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineDocumentHighlights(t, null!, U.ToAny(f.Ranges()));
            }
            finally
            {
                done();
            }
        });
    }

}
