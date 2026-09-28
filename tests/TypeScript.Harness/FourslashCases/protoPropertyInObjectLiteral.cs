// ported from: internal/fourslash/tests/protoPropertyInObjectLiteral_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ProtoPropertyInObjectLiteralCase
{
    public static void TestProtoPropertyInObjectLiteral(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("var o1 = {\n    \"__proto__\": 10\n};\nvar o2 = {\n    __proto__: 10\n};\no1./*1*/\no2./*2*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "__proto__", Detail = "(property) \"__proto__\": number" }] } });
                f.Insert(t, "__proto__ = 10;");
                f.VerifyQuickInfoAt(t, "1", "(property) \"__proto__\": number", "");
                f.VerifyCompletions(t, "2", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "__proto__", Detail = "(property) __proto__: number" }] } });
                f.Insert(t, "__proto__ = 10;");
                f.VerifyQuickInfoAt(t, "2", "(property) __proto__: number", "");
            }
            finally
            {
                done();
            }
        });
    }

}
