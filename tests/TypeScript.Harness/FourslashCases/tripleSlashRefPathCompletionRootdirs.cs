// ported from: internal/fourslash/tests/tripleSlashRefPathCompletionRootdirs_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class TripleSlashRefPathCompletionRootdirsCase
{
    public static void TestTripleSlashRefPathCompletionRootdirs(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @rootDirs: sub/src1,src2\n// @Filename: src2/test0.ts\n/// <reference path=\"./mo/*0*/\n// @Filename: src2/module0.ts\nexport var w = 0;\n// @Filename: sub/src1/module1.ts\nexport var x = 0;\n// @Filename: sub/src1/module2.ts\nexport var y = 0;\n// @Filename: sub/src1/more/module3.ts\nexport var z = 0;\n// @Filename: f1.ts\n/*f1*/\n// @Filename: f2.tsx\n/*f2*/\n// @Filename: folder/f1.ts\n/*subf1*/\n// @Filename: f3.js\n/*f3*/\n// @Filename: f4.jsx\n/*f4*/\n// @Filename: e1.ts\n/*e1*/\n// @Filename: e2.js\n/*e2*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "0", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["module0.ts"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
