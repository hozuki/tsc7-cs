// ported from: internal/fourslash/tests/navigateToImport_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class NavigateToImportCase
{
    public static void TestNavigateToImport(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n// @Filename: library.ts\nexport function [|foo|]() {}\nexport function [|bar|]() {}\n// @Filename: user.ts\nimport {foo, bar as [|baz|]} from './library';"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyWorkspaceSymbol(t, [new F.VerifyWorkspaceSymbolCase { Pattern = "foo", Preferences = null!, Exact = [new LSP.SymbolInformation { Name = "foo", Kind = LSP.SymbolKind.Function, Location = f.Ranges()[0].LSLocation() }] }, new F.VerifyWorkspaceSymbolCase { Pattern = "bar", Preferences = null!, Exact = [new LSP.SymbolInformation { Name = "bar", Kind = LSP.SymbolKind.Function, Location = f.Ranges()[1].LSLocation() }] }, new F.VerifyWorkspaceSymbolCase { Pattern = "baz", Preferences = null!, Exact = [new LSP.SymbolInformation { Name = "baz", Kind = LSP.SymbolKind.Variable, Location = f.Ranges()[2].LSLocation() }] }]);
            }
            finally
            {
                done();
            }
        });
    }

}
