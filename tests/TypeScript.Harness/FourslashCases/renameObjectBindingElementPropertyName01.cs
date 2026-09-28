// ported from: internal/fourslash/tests/renameObjectBindingElementPropertyName01_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameObjectBindingElementPropertyName01Case
{
    public static void TestRenameObjectBindingElementPropertyName01(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface I {\n    [|[|{| \"contextRangeIndex\": 0 |}property1|]: number;|]\n    property2: string;\n}\n\nvar foo: I;\n[|var { [|{| \"contextRangeIndex\": 2 |}property1|]: prop1 } = foo;|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineRenameAtRangesWithText(t, null!, "property1");
            }
            finally
            {
                done();
            }
        });
    }

}
