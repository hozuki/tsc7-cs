// ported from: internal/fourslash/tests/completionsGenericIndexedAccess6_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsGenericIndexedAccess6Case
{
    public static void TestCompletionsGenericIndexedAccess6(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: component.tsx\ninterface CustomElements {\n  'component-one': {\n      foo?: string;\n  },\n  'component-two': {\n      bar?: string;\n  }\n}\n\ntype Options<T extends keyof CustomElements> = { kind: T } & Required<{ x: CustomElements[(T extends string ? T : never) & string] }['x']>;\n\ndeclare function Component<T extends keyof CustomElements>(props: Options<T>): void;\n\nconst c = <Component /**/ kind=\"component-one\" />"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "foo" }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
