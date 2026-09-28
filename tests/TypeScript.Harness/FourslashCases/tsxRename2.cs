// ported from: internal/fourslash/tests/tsxRename2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class TsxRename2Case
{
    public static void TestTsxRename2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("//@Filename: file.tsx\ndeclare namespace JSX {\n    interface Element { }\n    interface IntrinsicElements {\n        div: {\n            [|[|{| \"contextRangeIndex\": 0 |}name|]?: string;|]\n            isOpen?: boolean;\n        };\n        span: { n: string; };\n    }\n}\nvar x = <div [|[|{| \"contextRangeIndex\": 2 |}name|]=\"hello\"|] />;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineRenameAtRangesWithText(t, null!, "name");
            }
            finally
            {
                done();
            }
        });
    }

}
