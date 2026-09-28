// ported from: internal/fourslash/tests/completionsUnionStringLiteralProperty_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsUnionStringLiteralPropertyCase
{
    public static void TestCompletionsUnionStringLiteralProperty(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("type Foo = { a: 0, b: 'x' } | { a: 0, b: 'y' } | { a: 1, b: 'z' };\nconst foo: Foo = { a: 0, b: '/*1*/' }\n\ntype Bar = { a: 0, b: 'fx' } | { a: 0, b: 'fy' } | { a: 1, b: 'fz' };\nconst bar: Bar = { a: 0, b: 'f/*2*/' }\n\ntype Baz = { x: 0, y: 0, z: 'a' } | { x: 0, y: 1, z: 'b' } | { x: 1, y: 0, z: 'c' } | { x: 1, y: 1, z: 'd' };\nconst baz1: Baz = { z: '/*3*/' };\nconst baz2: Baz = { x: 0, z: '/*4*/' };\nconst baz3: Baz = { x: 0, y: 1, z: '/*5*/' };\nconst baz4: Baz = { x: 2, y: 1, z: '/*6*/' };"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["x", "y"], Excludes = ["z"] } });
                f.VerifyCompletions(t, "2", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["fx", "fy"], Excludes = ["fz"] } });
                f.VerifyCompletions(t, "3", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["a", "b", "c", "d"] } });
                f.VerifyCompletions(t, "4", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["a", "b"], Excludes = ["c", "d"] } });
                f.VerifyCompletions(t, "5", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["b"], Excludes = ["a", "c", "d"] } });
                f.VerifyCompletions(t, "6", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["b", "d"], Excludes = ["a", "c"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
