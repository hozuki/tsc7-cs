// ported from: internal/fourslash/tests/completionsGenericIndexedAccess5_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsGenericIndexedAccess5Case
{
    public static void TestCompletionsGenericIndexedAccess5(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface CustomElements {\n  'component-one': {\n      foo?: string;\n  },\n  'component-two': {\n      bar?: string;\n  }\n}\n\ninterface Options<T extends keyof CustomElements> {\n    props?: {} & { x: CustomElements[(T extends string ? T : never) & string][] }['x'];\n}\n\ndeclare function f<T extends keyof CustomElements>(k: T, options: Options<T>): void;\n\nf(\"component-one\", {\n    props: [{\n        /**/\n    }]\n})"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "foo?", InsertText = "foo", FilterText = "foo", SortText = LS.SortTextOptionalMember }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
