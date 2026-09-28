// ported from: internal/fourslash/tests/navigationItemsExactMatch2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class NavigationItemsExactMatch2Case
{
    public static void TestNavigationItemsExactMatch2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("module Shapes {\n    class [|Point|] {\n        private [|_origin|] = 0.0;\n        private [|distanceFromA|] = 0.0;\n\n        get [|distance1|](distanceParam): number {\n            var [|distanceLocal|];\n            return 0;\n        }\n    }\n}\n\nvar [|point|] = new Shapes.Point();\nfunction [|distance2|](distanceParam1): void {\n    var [|distanceLocal1|];\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyWorkspaceSymbol(t, [new F.VerifyWorkspaceSymbolCase { Pattern = "point", Preferences = null!, Exact = [new LSP.SymbolInformation { Name = "Point", Kind = LSP.SymbolKind.Class, Location = f.Ranges()[0].LSLocation(), ContainerName = "Shapes" }, new LSP.SymbolInformation { Name = "point", Kind = LSP.SymbolKind.Variable, Location = f.Ranges()[5].LSLocation() }] }, new F.VerifyWorkspaceSymbolCase { Pattern = "distance", Preferences = null!, Exact = [new LSP.SymbolInformation { Name = "distance1", Kind = LSP.SymbolKind.Property, Location = f.Ranges()[3].LSLocation(), ContainerName = "Point" }, new LSP.SymbolInformation { Name = "distance2", Kind = LSP.SymbolKind.Function, Location = f.Ranges()[6].LSLocation() }, new LSP.SymbolInformation { Name = "distanceFromA", Kind = LSP.SymbolKind.Property, Location = f.Ranges()[2].LSLocation(), ContainerName = "Point" }, new LSP.SymbolInformation { Name = "distanceLocal", Kind = LSP.SymbolKind.Variable, Location = f.Ranges()[4].LSLocation(), ContainerName = "distance1" }, new LSP.SymbolInformation { Name = "distanceLocal1", Kind = LSP.SymbolKind.Variable, Location = f.Ranges()[7].LSLocation(), ContainerName = "distance2" }] }, new F.VerifyWorkspaceSymbolCase { Pattern = "origin", Preferences = null!, Exact = [new LSP.SymbolInformation { Name = "_origin", Kind = LSP.SymbolKind.Property, Location = f.Ranges()[1].LSLocation(), ContainerName = "Point" }] }, new F.VerifyWorkspaceSymbolCase { Pattern = "square", Preferences = null!, Exact = [] }]);
            }
            finally
            {
                done();
            }
        });
    }

}
