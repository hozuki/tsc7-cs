// ported from: internal/fourslash/tests/getOutliningForSingleLineComments_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetOutliningForSingleLineCommentsCase
{
    public static void TestGetOutliningForSingleLineComments(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("[|// Single line comments at the start of the file\n// line 2\n// line 3\n// line 4|]\nmodule Sayings[| {\n\n    [|/*\n    */|]\n    [|// A sequence of\n    // single line|]\n    [|/*\n        and block\n    */|]\n    [|// comments\n    //|]\n    export class Sample[| {\n    }|]\n}|]\n\ninterface IFoo[| {\n    [|// all consecutive single line comments should be in one block regardless of their number or empty lines/spaces inbetween\n\n    // comment 2\n    // comment 3\n\n    //comment 4\n    /// comment 5\n    ///// comment 6\n\n    //comment 7\n    ///comment 8\n    // comment 9\n    // //comment 10\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n    // // //comment 11\n    // comment 12\n    // comment 13\n    // comment 14\n    // comment 15\n\n    // comment 16\n    // comment 17\n    // comment 18\n    // comment 19\n    // comment 20    \n    // comment 21|]\n\n    getDist(): number; // One single line comment should not be collapsed\n}|]\n\n// One single line comment should not be collapsed\nclass WithOneSingleLineComment[| {\n}|]\n\nfunction Foo()[| {\n   [|// comment 1\n     // comment 2|]\n    this.method = function (param)[| {\n    }|]\n\n   [|// comment 1\n     // comment 2|]\n    function method(param)[| {\n    }|]\n}|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOutliningSpans(t);
            }
            finally
            {
                done();
            }
        });
    }

}
