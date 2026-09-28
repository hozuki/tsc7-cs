// ported from: internal/fourslash/tests/completionListBuilderLocations_VariableDeclarations_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListBuilderLocations_VariableDeclarationsCase
{
    public static void TestCompletionListBuilderLocations_VariableDeclarations(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\nvar x = a/*var1*/\nvar x = (b/*var2*/\nvar x = (c, d/*var3*/\n var y : any = \"\", x = a/*var4*/\n var y : any = \"\", x = (a/*var5*/\nclass C{}\nvar y = new C(/*var6*/\n class C{}\n var y = new C(0, /*var7*/\nvar y = [/*var8*/\nvar y = [0, /*var9*/\nvar y = `${/*var10*/\nvar y = `${10} dd ${ /*var11*/\nvar y = 10; y=/*var12*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["var1"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = U.CompletionGlobalsPlus(["C", "y"], false) } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["var2", "var3", "var4", "var5", "var6", "var7", "var8", "var9", "var10", "var11", "var12"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = U.CompletionGlobalsPlus(["C", "x", "y"], false) } });
            }
            finally
            {
                done();
            }
        });
    }

}
