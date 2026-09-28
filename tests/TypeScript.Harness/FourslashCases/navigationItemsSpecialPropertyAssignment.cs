// ported from: internal/fourslash/tests/navigationItemsSpecialPropertyAssignment_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class NavigationItemsSpecialPropertyAssignmentCase
{
    public static void TestNavigationItemsSpecialPropertyAssignment(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @noLib: true\n// @allowJs: true\n// @Filename: /a.js\nexports.[|x|] = 0;\nexports.[|z|] = function() {};\nfunction Cls() {\n    this.[|instanceProp|] = 0;\n}\nCls.[|staticMethod|] = function() {};\nCls.[|staticProperty|] = 0;\nCls.prototype.[|instanceMethod|] = function() {};"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyWorkspaceSymbol(t, [new F.VerifyWorkspaceSymbolCase { Pattern = "x", Preferences = null!, Exact = [new LSP.SymbolInformation { Name = "x", Kind = LSP.SymbolKind.Variable, Location = f.Ranges()[0].LSLocation() }] }, new F.VerifyWorkspaceSymbolCase { Pattern = "z", Preferences = null!, Exact = [new LSP.SymbolInformation { Name = "z", Kind = LSP.SymbolKind.Variable, Location = f.Ranges()[1].LSLocation() }] }, new F.VerifyWorkspaceSymbolCase { Pattern = "instanceProp", Preferences = null!, Exact = [new LSP.SymbolInformation { Name = "instanceProp", Kind = LSP.SymbolKind.Property, Location = f.Ranges()[2].LSLocation(), ContainerName = "Cls" }] }, new F.VerifyWorkspaceSymbolCase { Pattern = "staticMethod", Preferences = null!, Exact = [new LSP.SymbolInformation { Name = "staticMethod", Kind = LSP.SymbolKind.Property, Location = f.Ranges()[3].LSLocation() }] }, new F.VerifyWorkspaceSymbolCase { Pattern = "staticProperty", Preferences = null!, Exact = [new LSP.SymbolInformation { Name = "staticProperty", Kind = LSP.SymbolKind.Property, Location = f.Ranges()[4].LSLocation() }] }, new F.VerifyWorkspaceSymbolCase { Pattern = "instanceMethod", Preferences = null!, Exact = [new LSP.SymbolInformation { Name = "instanceMethod", Kind = LSP.SymbolKind.Property, Location = f.Ranges()[5].LSLocation() }] }]);
            }
            finally
            {
                done();
            }
        });
    }

}
