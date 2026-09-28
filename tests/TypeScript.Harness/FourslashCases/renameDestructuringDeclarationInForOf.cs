// ported from: internal/fourslash/tests/renameDestructuringDeclarationInForOf_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameDestructuringDeclarationInForOfCase
{
    public static void TestRenameDestructuringDeclarationInForOf(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface I {\n    [|[|{| \"contextRangeIndex\": 0 |}property1|]: number;|]\n    property2: string;\n}\nvar elems: I[];\n\nfor ([|let { [|{| \"contextRangeIndex\": 2 |}property1|] } of elems|]) {\n    [|property1|]++;\n}\nfor ([|let { [|{| \"contextRangeIndex\": 5 |}property1|]: p2 } of elems|]) {\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineRename(t, null!, f.Ranges()[1], f.Ranges()[6], f.Ranges()[3], f.Ranges()[4]);
            }
            finally
            {
                done();
            }
        });
    }

}
