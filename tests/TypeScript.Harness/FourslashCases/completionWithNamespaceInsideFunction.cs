// ported from: internal/fourslash/tests/completionWithNamespaceInsideFunction_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionWithNamespaceInsideFunctionCase
{
    public static void TestCompletionWithNamespaceInsideFunction(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function f() {\n    namespace n {\n        interface I {\n            x: number\n        }\n        /*1*/\n    }\n    /*2*/\n}\n/*3*/\nfunction f2() {\n    namespace n2 {\n        class I2 {\n            x: number\n        }\n        /*11*/\n    }\n    /*22*/\n}\n/*33*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["1", "2", "3"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "f", Detail = "function f(): void" }], Excludes = ["n", "I"] } });
                f.VerifyCompletions(t, "11", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "f2", Detail = "function f2(): void" }, new LSP.CompletionItem { Label = "n2", Detail = "namespace n2" }, new LSP.CompletionItem { Label = "I2", Detail = "class I2" }] } });
                f.VerifyCompletions(t, "22", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "f2", Detail = "function f2(): void" }, new LSP.CompletionItem { Label = "n2", Detail = "namespace n2" }], Excludes = ["I2"] } });
                f.VerifyCompletions(t, "33", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "f2", Detail = "function f2(): void" }], Excludes = ["n2", "I2"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
