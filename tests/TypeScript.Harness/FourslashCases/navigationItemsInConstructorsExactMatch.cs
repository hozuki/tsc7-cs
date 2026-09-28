// ported from: internal/fourslash/tests/navigationItemsInConstructorsExactMatch_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class NavigationItemsInConstructorsExactMatchCase
{
    public static void TestNavigationItemsInConstructorsExactMatch(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @noLib: true\nclass Test {\n    private [|search1|]: number;\n    constructor(public [|search2|]: boolean, readonly [|search3|]: string, search4: string) {\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyWorkspaceSymbol(t, [new F.VerifyWorkspaceSymbolCase { Pattern = "search", Preferences = null!, Exact = [new LSP.SymbolInformation { Name = "search1", Kind = LSP.SymbolKind.Property, Location = f.Ranges()[0].LSLocation(), ContainerName = "Test" }, new LSP.SymbolInformation { Name = "search2", Kind = LSP.SymbolKind.Property, Location = f.Ranges()[1].LSLocation(), ContainerName = "Test" }, new LSP.SymbolInformation { Name = "search3", Kind = LSP.SymbolKind.Property, Location = f.Ranges()[2].LSLocation(), ContainerName = "Test" }] }]);
            }
            finally
            {
                done();
            }
        });
    }

}
