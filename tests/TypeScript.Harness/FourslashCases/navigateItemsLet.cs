// ported from: internal/fourslash/tests/navigateItemsLet_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class NavigateItemsLetCase
{
    public static void TestNavigateItemsLet(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @noLib: true\nlet [|c|] = 10;\nfunction foo() {\n    let [|d|] = 10;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyWorkspaceSymbol(t, [new F.VerifyWorkspaceSymbolCase { Pattern = "c", Preferences = null!, Exact = [new LSP.SymbolInformation { Name = "c", Kind = LSP.SymbolKind.Variable, Location = f.Ranges()[0].LSLocation() }] }, new F.VerifyWorkspaceSymbolCase { Pattern = "d", Preferences = null!, Exact = [new LSP.SymbolInformation { Name = "d", Kind = LSP.SymbolKind.Variable, Location = f.Ranges()[1].LSLocation(), ContainerName = "foo" }] }]);
            }
            finally
            {
                done();
            }
        });
    }

}
