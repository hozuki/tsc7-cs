// ported from: internal/fourslash/tests/completionListModuleMembers_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListModuleMembersCase
{
    public static void TestCompletionListModuleMembers(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8(" namespace Module {\n     var innerVariable = 1;\n     function innerFunction() { }\n     class innerClass { }\n     namespace innerModule { }\n     interface innerInterface {}\n     export var exportedVariable = 1;\n     export function exportedFunction() { }\n     export class exportedClass { }\n     export namespace exportedModule { export var exportedInnerModuleVariable = 1; }\n     export interface exportedInterface {}\n }\n\nModule./*ValueReference*/;\n\nvar x : Module./*TypeReference*/\n\nclass TestClass extends Module./*TypeReferenceInExtendsList*/ { }\n\ninterface TestInterface implements Module./*TypeReferenceInImplementsList*/ { }"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["ValueReference", "TypeReferenceInExtendsList"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = ["exportedFunction", "exportedVariable", "exportedClass", "exportedModule"] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["TypeReference", "TypeReferenceInImplementsList"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = ["exportedClass", "exportedInterface"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
