// ported from: internal/fourslash/tests/completionsMergedDeclarations2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsMergedDeclarations2Case
{
    public static void TestCompletionsMergedDeclarations2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class point {\n    constructor(public x: number, public y: number) { }\n}\nnamespace point {\n    export var origin = new point(0, 0);\n    export function equals(p1: point, p2: point) {\n        return p1.x == p2.x && p1.y == p2.y;\n    }\n}\nvar p1 = new point(0, 0);\nvar p2 = point./*1*/origin;\nvar b = point./*2*/equals(p1, p2);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["origin"] } });
                f.VerifyCompletions(t, "2", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["equals"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
