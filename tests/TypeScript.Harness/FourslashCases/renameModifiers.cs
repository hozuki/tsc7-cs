// ported from: internal/fourslash/tests/renameModifiers_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameModifiersCase
{
    public static void TestRenameModifiers(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("[|[|declare|] [|abstract|] class [|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeDelta\": -3 |}C1|] {\n    [|[|static|] [|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeDelta\": -2 |}a|];|]\n    [|[|readonly|] [|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeDelta\": -2 |}b|];|]\n    [|[|public|] [|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeDelta\": -2 |}c|];|]\n    [|[|protected|] [|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeDelta\": -2 |}d|];|]\n    [|[|private|] [|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeDelta\": -2 |}e|];|]\n}|]\n[|[|const|] enum [|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeDelta\": -2 |}E|] {\n}|]\n[|[|async|] function [|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeDelta\": -2 |}fn|]() {}|]\n[|[|export|] [|default|] class [|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeDelta\": -3 |}C2|] {}|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineRename(t, null!, f.Ranges()[1], f.Ranges()[2], f.Ranges()[5], f.Ranges()[8], f.Ranges()[11], f.Ranges()[14], f.Ranges()[17], f.Ranges()[20], f.Ranges()[23], f.Ranges()[26], f.Ranges()[27]);
            }
            finally
            {
                done();
            }
        });
    }

}
