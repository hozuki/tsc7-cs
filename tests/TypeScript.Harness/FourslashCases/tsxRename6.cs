// ported from: internal/fourslash/tests/tsxRename6_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class TsxRename6Case
{
    public static void TestTsxRename6(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("//@Filename: file.tsx\n// @jsx: preserve\n// @noLib: true\ndeclare namespace JSX {\n    interface Element { }\n    interface IntrinsicElements {\n    }\n    interface ElementAttributesProperty { props; }\n}\ninterface OptionPropBag {\n    propx: number\n    propString: string\n    optional?: boolean\n}\n[|declare function [|{| \"contextRangeIndex\": 0 |}Opt|](attributes: OptionPropBag): JSX.Element;|]\nlet opt = [|<[|{| \"contextRangeIndex\": 2 |}Opt|] />|];\nlet opt1 = [|<[|{| \"contextRangeIndex\": 4 |}Opt|] propx={100} propString />|];\nlet opt2 = [|<[|{| \"contextRangeIndex\": 6 |}Opt|] propx={100} optional/>|];\nlet opt3 = [|<[|{| \"contextRangeIndex\": 8 |}Opt|] wrong />|];\nlet opt4 = [|<[|{| \"contextRangeIndex\": 10 |}Opt|] propx={100} propString=\"hi\" />|];"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineRenameAtRangesWithText(t, null!, "Opt");
            }
            finally
            {
                done();
            }
        });
    }

}
