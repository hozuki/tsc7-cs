// ported from: internal/fourslash/tests/completionInsideObjectLiteralExpressionWithInstantiatedClassType_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionInsideObjectLiteralExpressionWithInstantiatedClassTypeCase
{
    public static void TestCompletionInsideObjectLiteralExpressionWithInstantiatedClassType(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class C1 {\n    public a: string;\n    protected b: string;\n    private c: string;\n\n    constructor(a: string, b = \"\", c = \"\") {\n        this.a = a;\n        this.b = b;\n        this.c = c;\n    }\n}\nclass C2 {\n    public a: string;\n    constructor(a: string) {\n        this.a = a;\n    }\n}\nfunction f1(foo: C1 | C2 | { d: number }) {}\nf1({ /*1*/ });\nfunction f2(foo: C1 | C2) {}\nf2({ /*2*/ });\n\nfunction f3(foo: C2) {}\nf3({ /*3*/ });"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["a", "d"] } });
                f.VerifyCompletions(t, "2", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["a"] } });
                f.VerifyCompletions(t, "3", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["a"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
