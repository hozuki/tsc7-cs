// ported from: internal/fourslash/tests/stringLiteralCompletionsForGenericConditionalTypesUsingTemplateLiteralTypes_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class StringLiteralCompletionsForGenericConditionalTypesUsingTemplateLiteralTypesCase
{
    public static void TestStringLiteralCompletionsForGenericConditionalTypesUsingTemplateLiteralTypes(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("type PathOf<T, K extends string, P extends string = \"\"> =\n  K extends `${infer U}.${infer V}`\n    ? U extends keyof T ? PathOf<T[U], V, `${P}${U}.`> : `${P}${keyof T & (string | number)}`\n    : K extends keyof T ? `${P}${K}` : `${P}${keyof T & (string | number)}`;\n\ndeclare function consumer<K extends string>(path: PathOf<{a: string, b: {c: string}}, K>) : number;\n\nconsumer('b./*ts*/')"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["ts"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["a", "b", "b.c"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
