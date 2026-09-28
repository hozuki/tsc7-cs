// ported from: internal/fourslash/tests/navto_excludeLib2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class Navto_excludeLib2Case
{
    public static void TestNavto_excludeLib2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @filename: /index.ts\nimport { someName as [|weirdName|] } from \"bar\";\n// @filename: /tsconfig.json\n{}\n// @filename: /node_modules/bar/index.d.ts\nexport const someName: number;\n// @filename: /node_modules/bar/package.json\n{}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyWorkspaceSymbol(t, [new F.VerifyWorkspaceSymbolCase { Pattern = "weirdName", Preferences = new LSUP.UserPreferences { ExcludeLibrarySymbolsInNavTo = TRI.TSFalse }, Exact = [new LSP.SymbolInformation { Name = "weirdName", Kind = LSP.SymbolKind.Variable, Location = f.Ranges()[0].LSLocation() }] }]);
                f.VerifyWorkspaceSymbol(t, [new F.VerifyWorkspaceSymbolCase { Pattern = "weirdName", Preferences = null!, Exact = [new LSP.SymbolInformation { Name = "weirdName", Kind = LSP.SymbolKind.Variable, Location = f.Ranges()[0].LSLocation() }] }]);
            }
            finally
            {
                done();
            }
        });
    }

}
