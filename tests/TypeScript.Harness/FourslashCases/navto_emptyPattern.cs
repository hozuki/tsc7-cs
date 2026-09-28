// ported from: internal/fourslash/tests/navto_emptyPattern_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class Navto_emptyPatternCase
{
    public static void TestNavto_emptyPattern(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @filename: foo.ts\nconst [|x|]: number = 1;\nfunction [|y|](x: string): string { return x; }"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyWorkspaceSymbol(t, [new F.VerifyWorkspaceSymbolCase { Pattern = "", Preferences = null!, Exact = [new LSP.SymbolInformation { Name = "x", Kind = LSP.SymbolKind.Variable, Location = f.Ranges()[0].LSLocation() }, new LSP.SymbolInformation { Name = "y", Kind = LSP.SymbolKind.Function, Location = f.Ranges()[1].LSLocation() }] }]);
            }
            finally
            {
                done();
            }
        });
    }

}
