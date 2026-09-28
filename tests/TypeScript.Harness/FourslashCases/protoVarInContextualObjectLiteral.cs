// ported from: internal/fourslash/tests/protoVarInContextualObjectLiteral_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ProtoVarInContextualObjectLiteralCase
{
    public static void TestProtoVarInContextualObjectLiteral(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("var o1 : {\n    __proto__: number;\n    p: number;\n} = {\n        /*1*/\n    };\nvar o2: {\n    __proto__: number;\n    p: number;\n} = {\n        /*2*/\n    };\nvar o3: {\n    \"__proto__\": number;\n    p: number;\n} = {\n        /*3*/\n    };\nvar o4: {\n    \"__proto__\": number;\n    p: number;\n} = {\n        /*4*/\n    };\nvar o5: {\n    __proto__: number;\n    ___proto__: string;\n    p: number;\n} = {\n        /*5*/\n    };\nvar o6: {\n    __proto__: number;\n    ___proto__: string;\n    p: number;\n} = {\n        /*6*/\n    };"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = [new LSP.CompletionItem { Label = "__proto__", Detail = "(property) __proto__: number" }, new LSP.CompletionItem { Label = "p", Detail = "(property) p: number" }] } });
                f.Insert(t, "__proto__: 10,");
                f.VerifyCompletions(t, null!, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "p", Detail = "(property) p: number" }] } });
                f.VerifyCompletions(t, "2", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = [new LSP.CompletionItem { Label = "__proto__", Detail = "(property) __proto__: number" }, new LSP.CompletionItem { Label = "p", Detail = "(property) p: number" }] } });
                f.Insert(t, "\"__proto__\": 10,");
                f.VerifyCompletions(t, null!, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "p", Detail = "(property) p: number" }] } });
                f.VerifyCompletions(t, "3", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = [new LSP.CompletionItem { Label = "__proto__", Detail = "(property) \"__proto__\": number" }, new LSP.CompletionItem { Label = "p", Detail = "(property) p: number" }] } });
                f.Insert(t, "__proto__: 10,");
                f.VerifyCompletions(t, null!, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "p", Detail = "(property) p: number" }] } });
                f.VerifyCompletions(t, "4", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = [new LSP.CompletionItem { Label = "__proto__", Detail = "(property) \"__proto__\": number" }, new LSP.CompletionItem { Label = "p", Detail = "(property) p: number" }] } });
                f.Insert(t, "\"__proto__\": 10,");
                f.VerifyCompletions(t, null!, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "p", Detail = "(property) p: number" }] } });
                f.VerifyCompletions(t, "5", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = [new LSP.CompletionItem { Label = "__proto__", Detail = "(property) __proto__: number" }, new LSP.CompletionItem { Label = "___proto__", Detail = "(property) ___proto__: string" }, new LSP.CompletionItem { Label = "p", Detail = "(property) p: number" }] } });
                f.Insert(t, "__proto__: 10,");
                f.VerifyCompletions(t, null!, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = [new LSP.CompletionItem { Label = "___proto__", Detail = "(property) ___proto__: string" }, new LSP.CompletionItem { Label = "p", Detail = "(property) p: number" }] } });
                f.Insert(t, "\"___proto__\": \"10\",");
                f.VerifyCompletions(t, null!, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "p", Detail = "(property) p: number" }] } });
                f.VerifyCompletions(t, "6", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = [new LSP.CompletionItem { Label = "__proto__", Detail = "(property) __proto__: number" }, new LSP.CompletionItem { Label = "___proto__", Detail = "(property) ___proto__: string" }, new LSP.CompletionItem { Label = "p", Detail = "(property) p: number" }] } });
                f.Insert(t, "___proto__: \"10\",");
                f.VerifyCompletions(t, null!, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = [new LSP.CompletionItem { Label = "__proto__", Detail = "(property) __proto__: number" }, new LSP.CompletionItem { Label = "p", Detail = "(property) p: number" }] } });
                f.Insert(t, "\"__proto__\": 10,");
                f.VerifyCompletions(t, null!, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "p", Detail = "(property) p: number" }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
