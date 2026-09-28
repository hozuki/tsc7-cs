// ported from: internal/fourslash/tests/protoVarVisibleWithOuterScopeUnderscoreProto_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ProtoVarVisibleWithOuterScopeUnderscoreProtoCase
{
    public static void TestProtoVarVisibleWithOuterScopeUnderscoreProto(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// outer\nvar ___proto__ = 10;\nfunction foo() {\n    var __proto__ = \"hello\";\n    /**/\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "__proto__", Detail = "(local var) __proto__: string" }, new LSP.CompletionItem { Label = "___proto__", Detail = "var ___proto__: number" }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
