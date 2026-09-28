// ported from: internal/fourslash/tests/renameLocationsForClassExpression01_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameLocationsForClassExpression01Case
{
    public static void TestRenameLocationsForClassExpression01(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class Foo {\n}\n\nvar x = [|class [|{| \"contextRangeIndex\": 0 |}Foo|] {\n    doIt() {\n        return [|Foo|];\n    }\n\n    static doItStatically() {\n        return [|Foo|].y;\n    }\n}|]\n\nvar y = class {\n   getSomeName() {\n      return Foo\n   }\n}\nvar z = class Foo {}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineRenameAtRangesWithText(t, null!, "Foo");
            }
            finally
            {
                done();
            }
        });
    }

}
