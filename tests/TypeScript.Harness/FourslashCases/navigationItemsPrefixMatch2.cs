// ported from: internal/fourslash/tests/navigationItemsPrefixMatch2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class NavigationItemsPrefixMatch2Case
{
    public static void TestNavigationItemsPrefixMatch2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\nnamespace Shapes {\n    export class Point {\n        private [|originality|] = 0.0;\n        private [|distanceFromOrig|] = 0.0;\n        get [|distanceFarFarAway|](distanceFarFarAwayParam: number): number {\n            var [|distanceFarFarAwayLocal|];\n            return 0;\n        }\n    }\n}\nvar pointsSquareBox = new Shapes.Point();\nfunction PointsFunc(): void {\n var pointFuncLocal;\n}\ninterface [|OriginI|] {\n    123;\n    [|origin1|];\n    public [|_distance|](distanceParam): void;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyWorkspaceSymbol(t, [new F.VerifyWorkspaceSymbolCase { Pattern = "origin", Preferences = null!, Exact = [new LSP.SymbolInformation { Name = "origin1", Kind = LSP.SymbolKind.Property, Location = f.Ranges()[5].LSLocation(), ContainerName = "OriginI" }, new LSP.SymbolInformation { Name = "originality", Kind = LSP.SymbolKind.Property, Location = f.Ranges()[0].LSLocation(), ContainerName = "Point" }, new LSP.SymbolInformation { Name = "OriginI", Kind = LSP.SymbolKind.Interface, Location = f.Ranges()[4].LSLocation() }] }, new F.VerifyWorkspaceSymbolCase { Pattern = "distance", Preferences = null!, Exact = [new LSP.SymbolInformation { Name = "distanceFarFarAway", Kind = LSP.SymbolKind.Property, Location = f.Ranges()[2].LSLocation(), ContainerName = "Point" }, new LSP.SymbolInformation { Name = "distanceFarFarAwayLocal", Kind = LSP.SymbolKind.Variable, Location = f.Ranges()[3].LSLocation(), ContainerName = "distanceFarFarAway" }, new LSP.SymbolInformation { Name = "distanceFromOrig", Kind = LSP.SymbolKind.Property, Location = f.Ranges()[1].LSLocation(), ContainerName = "Point" }, new LSP.SymbolInformation { Name = "_distance", Kind = LSP.SymbolKind.Method, Location = f.Ranges()[6].LSLocation(), ContainerName = "OriginI" }] }, new F.VerifyWorkspaceSymbolCase { Pattern = "mPointThatIJustInitiated wrongKeyWord", Preferences = null!, Exact = [] }]);
            }
            finally
            {
                done();
            }
        });
    }

}
