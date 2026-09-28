// ported from: internal/fourslash/tests/renameAliasExternalModule_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameAliasExternalModuleCase
{
    public static void TestRenameAliasExternalModule(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: a.ts\nnamespace SomeModule { export class SomeClass { } }\nexport = SomeModule;\n// @Filename: b.ts\n[|import [|{| \"contextRangeIndex\": 0 |}M|] = require(\"./a\");|]\nimport C = [|M|].SomeClass;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineRenameAtRangesWithText(t, null!, "M");
            }
            finally
            {
                done();
            }
        });
    }

}
