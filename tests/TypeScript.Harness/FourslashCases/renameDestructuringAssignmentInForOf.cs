// ported from: internal/fourslash/tests/renameDestructuringAssignmentInForOf_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameDestructuringAssignmentInForOfCase
{
    public static void TestRenameDestructuringAssignmentInForOf(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: false\ninterface I {\n    [|[|{| \"contextRangeIndex\": 0 |}property1|]: number;|]\n    property2: string;\n}\nvar elems: I[];\n\nvar [|[|{| \"contextRangeIndex\": 2 |}property1|]: number|], p2: number;\nfor ([|{ [|{| \"contextRangeIndex\": 4 |}property1|] } of elems|]) {\n    [|property1|]++;\n}\nfor ([|{ [|{| \"contextRangeIndex\": 7 |}property1|]: p2 } of elems|]) {\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNoErrors(t);
                f.VerifyBaselineRename(t, null!, f.Ranges()[1], f.Ranges()[8], f.Ranges()[3], f.Ranges()[5], f.Ranges()[6]);
            }
            finally
            {
                done();
            }
        });
    }

}
