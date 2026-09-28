// ported from: internal/fourslash/tests/tsxRename4_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class TsxRename4Case
{
    public static void TestTsxRename4(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @jsx: preserve\n//@Filename: file.tsx\ndeclare namespace JSX {\n    interface Element {}\n    interface IntrinsicElements {\n        div: {};\n    }\n}\n[|class [|{| \"contextRangeIndex\": 0 |}MyClass|] {}|]\n\n[|<[|{| \"contextRangeIndex\": 2 |}MyClass|]></[|{| \"contextRangeIndex\": 2 |}MyClass|]>|];\n[|<[|{| \"contextRangeIndex\": 5 |}MyClass|]/>|];\n\n[|<[|{| \"contextRangeIndex\": 7 |}div|]> </[|{| \"contextRangeIndex\": 7 |}div|]>|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNoErrors(t);
                f.VerifyBaselineRenameAtRangesWithText(t, null!, "MyClass", "div");
            }
            finally
            {
                done();
            }
        });
    }

}
