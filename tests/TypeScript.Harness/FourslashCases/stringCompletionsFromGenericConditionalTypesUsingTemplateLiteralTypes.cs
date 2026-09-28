// ported from: internal/fourslash/tests/stringCompletionsFromGenericConditionalTypesUsingTemplateLiteralTypes_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class StringCompletionsFromGenericConditionalTypesUsingTemplateLiteralTypesCase
{
    public static void TestStringCompletionsFromGenericConditionalTypesUsingTemplateLiteralTypes(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @stableTypeOrdering: true\n// @strict: true\ntype keyword = \"foo\" | \"bar\" | \"baz\"\n\ntype validateString<s> = s extends keyword\n    ? s\n    : s extends `${infer left extends keyword}|${infer right}`\n    ? right extends keyword\n        ? s\n        : `${left}|${keyword}`\n    : keyword\n\ntype isUnknown<t> = unknown extends t\n    ? [t] extends [{}]\n        ? false\n        : true\n    : false\n\ntype validate<def> = def extends string\n    ? validateString<def>\n    : isUnknown<def> extends true\n    ? keyword\n    : {\n          [k in keyof def]: validate<def[k]>\n      }\nconst parse = <def>(def: validate<def>) => def\nconst shallowExpression = parse(\"foo|/*ts*/\")\nconst nestedExpression = parse({ prop: \"foo|/*ts2*/\" })"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["ts"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["bar", "baz", "foo", "foo|bar", "foo|baz", "foo|foo"] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["ts2"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["foo|bar", "foo|baz", "foo|foo"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
