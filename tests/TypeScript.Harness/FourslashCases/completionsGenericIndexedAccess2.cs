// ported from: internal/fourslash/tests/completionsGenericIndexedAccess2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsGenericIndexedAccess2Case
{
    public static void TestCompletionsGenericIndexedAccess2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("export type GetMethodsForType<T, G extends string> = { [K in keyof T]:\n  T[K] extends () => any ? { name: K, group: G, } : T[K] extends (s: infer U) => any ? { name: K, group: G, payload: U } : never }[keyof T];\n\n\nclass Sample {\n  count = 0;\n  books: { name: string, year: number }[] = []\n  increment() {\n      this.count++\n      this.count++\n  }\n\n  addBook(book: Sample[\"books\"][0]) {\n      this.books.push(book)\n  }\n}\nexport declare function testIt<T, G extends string>(): (input: any, method: GetMethodsForType<T, G>) => any\n\n\nconst t = testIt<Sample, \"Sample\">()\n\nconst i = t(null, { name: \"addBook\", group: \"Sample\", payload: { /**/ } })"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "name" }, new LSP.CompletionItem { Label = "year" }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
