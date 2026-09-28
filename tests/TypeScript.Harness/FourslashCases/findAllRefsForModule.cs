// ported from: internal/fourslash/tests/findAllRefsForModule_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsForModuleCase
{
    public static void TestFindAllRefsForModule(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: /a.ts\nexport const x = 0;\n// @Filename: /b.ts\n[|import { x } from \"/*0*/[|{| \"contextRangeIndex\": 0 |}./a|]\";|]\n// @Filename: /c/sub.js\n[|const a = require(\"/*1*/[|{| \"contextRangeIndex\": 2 |}../a|]\");|]\n// @Filename: /d.ts\n /// <reference path=\"/*2*/[|./a.ts|]\" />"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "0", "1", "2");
                f.VerifyBaselineDocumentHighlightsWithOptions(t, null!, ["/b.ts", "/c/sub.js", "/d.ts"], f.Ranges()[1], f.Ranges()[3], f.Ranges()[4]);
            }
            finally
            {
                done();
            }
        });
    }

}
