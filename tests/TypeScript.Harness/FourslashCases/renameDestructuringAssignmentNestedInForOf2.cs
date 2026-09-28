// ported from: internal/fourslash/tests/renameDestructuringAssignmentNestedInForOf2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameDestructuringAssignmentNestedInForOf2Case
{
    public static void TestRenameDestructuringAssignmentNestedInForOf2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface MultiRobot {\n    name: string;\n    skills: {\n        [|[|{| \"contextRangeIndex\": 0 |}primary|]: string;|]\n        secondary: string;\n    };\n}\nlet multiRobots: MultiRobot[], [|[|{| \"contextRangeIndex\": 2 |}primary|]: string|];\nfor ([|{ skills: { [|{| \"contextRangeIndex\": 4 |}primary|]: primaryA, secondary: secondaryA } } of multiRobots|]) {\n    console.log(primaryA);\n}\nfor ([|{ skills: { [|{| \"contextRangeIndex\": 6 |}primary|], secondary } } of multiRobots|]) {\n    console.log([|primary|]);\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineRename(t, null!, f.Ranges()[1], f.Ranges()[5], f.Ranges()[3], f.Ranges()[7], f.Ranges()[8]);
            }
            finally
            {
                done();
            }
        });
    }

}
