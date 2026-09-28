// ported from: internal/fourslash/tests/tsxRename9_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class TsxRename9Case
{
    public static void TestTsxRename9(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("//@Filename: file.tsx\n// @jsx: preserve\n// @noLib: true\ndeclare namespace JSX {\n    interface Element { }\n    interface IntrinsicElements {\n    }\n    interface ElementAttributesProperty { props; }\n}\ninterface ClickableProps {\n    children?: string;\n    className?: string;\n}\ninterface ButtonProps extends ClickableProps {\n    [|[|{| \"contextRangeIndex\": 0 |}onClick|](event?: React.MouseEvent<HTMLButtonElement>): void;|]\n}\ninterface LinkProps extends ClickableProps {\n    [|[|{| \"contextRangeIndex\": 2 |}goTo|]: string;|]\n}\n[|declare function [|{| \"contextRangeIndex\": 4 |}MainButton|](buttonProps: ButtonProps): JSX.Element;|]\n[|declare function [|{| \"contextRangeIndex\": 6 |}MainButton|](linkProps: LinkProps): JSX.Element;|]\n[|declare function [|{| \"contextRangeIndex\": 8 |}MainButton|](props: ButtonProps | LinkProps): JSX.Element;|]\nlet opt = [|<[|{| \"contextRangeIndex\": 10 |}MainButton|] />|];\nlet opt = [|<[|{| \"contextRangeIndex\": 12 |}MainButton|] children=\"chidlren\" />|];\nlet opt = [|<[|{| \"contextRangeIndex\": 14 |}MainButton|] [|[|{| \"contextRangeIndex\": 16 |}onClick|]={()=>{}}|] />|];\nlet opt = [|<[|{| \"contextRangeIndex\": 18 |}MainButton|] [|[|{| \"contextRangeIndex\": 20 |}onClick|]={()=>{}}|] [|ignore-prop|] />|];\nlet opt = [|<[|{| \"contextRangeIndex\": 23 |}MainButton|] [|[|{| \"contextRangeIndex\": 25 |}goTo|]=\"goTo\"|] />|];\nlet opt = [|<[|{| \"contextRangeIndex\": 27 |}MainButton|] [|wrong|] />|];"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineRenameAtRangesWithText(t, null!, "onClick", "goTo", "MainButton", "ignore-prop", "wrong");
            }
            finally
            {
                done();
            }
        });
    }

}
