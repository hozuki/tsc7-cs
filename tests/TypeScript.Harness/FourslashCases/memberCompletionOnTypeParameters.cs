// ported from: internal/fourslash/tests/memberCompletionOnTypeParameters_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class MemberCompletionOnTypeParametersCase
{
    public static void TestMemberCompletionOnTypeParameters(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface IFoo {\n    x: number;\n    y: string;\n}\n\nfunction foo<S, T extends IFoo, U extends Object, V extends IFoo>() {\n    var s:S, t: T, u: U, v: V;\n    s./*S*/;    // no constraint, no completion\n    t./*T*/;    // IFoo\n    u./*U*/;    // IFoo\n    v./*V*/;    // IFoo\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "S", null!);
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["T", "V"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "x", Detail = "(property) IFoo.x: number" }, new LSP.CompletionItem { Label = "y", Detail = "(property) IFoo.y: string" }] } });
                f.VerifyCompletions(t, "U", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = ["constructor", "toString", "toLocaleString", "valueOf", "hasOwnProperty", "isPrototypeOf", "propertyIsEnumerable"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
