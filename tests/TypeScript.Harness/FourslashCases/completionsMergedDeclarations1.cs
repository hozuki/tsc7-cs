// ported from: internal/fourslash/tests/completionsMergedDeclarations1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsMergedDeclarations1Case
{
    public static void TestCompletionsMergedDeclarations1(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\ninterface Point {\n    x: number;\n    y: number;\n}\nfunction point(x: number, y: number): Point {\n    return { x: x, y: y };\n}\nnamespace point {\n    export var origin = point(0, 0);\n    export function equals(p1: Point, p2: Point) {\n        return p1.x == p2.x && p1.y == p2.y;\n    }\n}\nvar p1 = /*1*/point(0, 0);\nvar p2 = point./*2*/origin;\nvar b = point./*3*/equals(p1, p2);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["point"] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["2", "3"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = U.CompletionFunctionMembersWithPrototypePlus(["equals", "origin"]) } });
            }
            finally
            {
                done();
            }
        });
    }

}
