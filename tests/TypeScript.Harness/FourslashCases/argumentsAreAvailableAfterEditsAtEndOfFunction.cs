// ported from: internal/fourslash/tests/argumentsAreAvailableAfterEditsAtEndOfFunction_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ArgumentsAreAvailableAfterEditsAtEndOfFunctionCase
{
    public static void TestArgumentsAreAvailableAfterEditsAtEndOfFunction(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("namespace Test1 {\n\tclass Person {\n\t\tchildren: string[];\n\t\tconstructor(public name: string, children: string[]) {\n\t\t\t/**/\n\t\t}\n\t}\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.Insert(t, "this.children = ch");
                f.VerifyCompletions(t, null!, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "children", Detail = "(parameter) children: string[]" }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
