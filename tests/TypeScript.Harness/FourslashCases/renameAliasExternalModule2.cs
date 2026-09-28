// ported from: internal/fourslash/tests/renameAliasExternalModule2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameAliasExternalModule2Case
{
    public static void TestRenameAliasExternalModule2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: a.ts\n[|module [|{| \"contextRangeIndex\": 0 |}SomeModule|] { export class SomeClass { } }|]\n[|export = [|{| \"contextRangeIndex\": 2 |}SomeModule|];|]\n// @Filename: b.ts\n[|import [|{| \"contextRangeIndex\": 4 |}M|] = require(\"./a\");|]\nimport C = [|M|].SomeClass;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineRename(t, null!, f.Ranges()[1], f.Ranges()[3], f.Ranges()[5], f.Ranges()[6]);
            }
            finally
            {
                done();
            }
        });
    }

}
