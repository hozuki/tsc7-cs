// ported from: internal/fourslash/tests/renameCommentsAndStrings4_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameCommentsAndStrings4Case
{
    public static void TestRenameCommentsAndStrings4(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("///<reference path=\"./Bar.ts\" />\n[|function [|{| \"contextRangeIndex\": 0 |}Bar|]() {\n    // This is a reference to [|Bar|] in a comment.\n    \"this is a reference to [|Bar|] in a string\";\n    `Foo [|Bar|] Baz.`;\n    {\n        const Bar = 0;\n        `[|Bar|] ba ${Bar} bara [|Bar|] berbobo ${Bar} araura [|Bar|] ara!`;\n    }\n}|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineRename(t, null!, f.Ranges()[1]);
            }
            finally
            {
                done();
            }
        });
    }

}
