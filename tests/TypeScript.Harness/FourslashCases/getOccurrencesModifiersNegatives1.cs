// ported from: internal/fourslash/tests/getOccurrencesModifiersNegatives1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetOccurrencesModifiersNegatives1Case
{
    public static void TestGetOccurrencesModifiersNegatives1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class C {\n    [|{| \"count\": 3 |}export|] foo;\n    [|{| \"count\": 3 |}declare|] bar;\n    [|{| \"count\": 3 |}export|] [|{| \"count\": 3 |}declare|] foobar;\n    [|{| \"count\": 3 |}declare|] [|{| \"count\": 3 |}export|] barfoo;\n\n    constructor([|{| \"count\": 9 |}export|] conFoo,\n                [|{| \"count\": 9 |}declare|] conBar,\n                [|{| \"count\": 9 |}export|] [|{| \"count\": 9 |}declare|] conFooBar,\n                [|{| \"count\": 9 |}declare|] [|{| \"count\": 9 |}export|] conBarFoo,\n                [|{| \"count\": 4 |}static|] sue,\n                [|{| \"count\": 4 |}static|] [|{| \"count\": 9 |}export|] [|{| \"count\": 9 |}declare|] sueFooBar,\n                [|{| \"count\": 4 |}static|] [|{| \"count\": 9 |}declare|] [|{| \"count\": 9 |}export|] sueBarFoo,\n                [|{| \"count\": 9 |}declare|] [|{| \"count\": 4 |}static|] [|{| \"count\": 9 |}export|] barSueFoo) {\n    }\n}\n\nnamespace m {\n    [|{| \"count\": 0 |}static|] a;\n    [|{| \"count\": 0 |}public|] b;\n    [|{| \"count\": 0 |}private|] c;\n    [|{| \"count\": 0 |}protected|] d;\n    [|{| \"count\": 0 |}static|] [|{| \"count\": 0 |}public|] [|{| \"count\": 0 |}private|] [|{| \"count\": 0 |}protected|] e;\n    [|{| \"count\": 0 |}public|] [|{| \"count\": 0 |}static|] [|{| \"count\": 0 |}protected|] [|{| \"count\": 0 |}private|] f;\n    [|{| \"count\": 0 |}protected|] [|{| \"count\": 0 |}static|] [|{| \"count\": 0 |}public|] g;\n}\n[|{| \"count\": 0 |}static|] a;\n[|{| \"count\": 0 |}public|] b;\n[|{| \"count\": 0 |}private|] c;\n[|{| \"count\": 0 |}protected|] d;\n[|{| \"count\": 0 |}static|] [|{| \"count\": 0 |}public|] [|{| \"count\": 0 |}private|] [|{| \"count\": 0 |}protected|] e;\n[|{| \"count\": 0 |}public|] [|{| \"count\": 0 |}static|] [|{| \"count\": 0 |}protected|] [|{| \"count\": 0 |}private|] f;\n[|{| \"count\": 0 |}protected|] [|{| \"count\": 0 |}static|] [|{| \"count\": 0 |}public|] g;"u8);
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
