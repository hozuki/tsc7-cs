// ported from: internal/fourslash/tests/tripleSlashRefPathCompletionAbsolutePaths_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class TripleSlashRefPathCompletionAbsolutePathsCase
{
    public static void TestTripleSlashRefPathCompletionAbsolutePaths(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /tests/cases/fourslash/tests/test0.ts\n/// <reference path=\"/tests/cases/f/*0*/\n// @Filename: /tests/cases/fourslash/tests/test1.ts\n/// <reference path=\"/tests/cases/fourslash/*1*/\n// @Filename: /tests/cases/fourslash/tests/test2.ts\n/// <reference path=\"/tests/cases/fourslash//*2*/\n// @Filename: /tests/cases/fourslash/f1.ts\n/*f1*/\n// @Filename: /tests/cases/fourslash/f2.tsx\n/*f2*/\n// @Filename: /tests/cases/fourslash/folder/f1.ts\n/*subf1*/\n// @Filename: /tests/cases/fourslash/f3.js\n/*f3*/\n// @Filename: /tests/cases/fourslash/f4.jsx\n/*f4*/\n// @Filename: /tests/cases/fourslash/e1.ts\n/*e1*/\n// @Filename: /tests/cases/fourslash/e2.js\n/*e2*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["0", "1"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["fourslash"] } });
                f.VerifyCompletions(t, "2", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["e1.ts", "f1.ts", "f2.tsx", "folder", "tests"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
