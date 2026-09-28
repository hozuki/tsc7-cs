// ported from: internal/fourslash/tests/renameDestructuringAssignmentNestedInFor_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameDestructuringAssignmentNestedInForCase
{
    public static void TestRenameDestructuringAssignmentNestedInFor(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface MultiRobot {\n    name: string;\n    skills: {\n        [|[|{| \"contextRangeIndex\": 0 |}primary|]: string;|]\n        secondary: string;\n    };\n}\ndeclare let multiRobot: MultiRobot, [|[|{| \"contextRangeIndex\": 2 |}primary|]: string|], secondary: string, primaryA: string, secondaryA: string, i: number;\nfor ([|{ skills: { [|{| \"contextRangeIndex\": 4 |}primary|]: primaryA, secondary: secondaryA } } = multiRobot|], i = 0; i < 1; i++) {\n    primaryA;\n}\nfor ([|{ skills: { [|{| \"contextRangeIndex\": 6 |}primary|], secondary } } = multiRobot|], i = 0; i < 1; i++) {\n    [|primary|];\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNoErrors(t);
                f.VerifyBaselineRename(t, null!, f.Ranges()[1], f.Ranges()[5], f.Ranges()[3], f.Ranges()[7], f.Ranges()[8]);
            }
            finally
            {
                done();
            }
        });
    }

}
