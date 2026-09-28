// ported from: internal/fourslash/tests/navigateToSymbolIterator_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class NavigateToSymbolIteratorCase
{
    public static void TestNavigateToSymbolIterator(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\nclass C {\n    [|[Symbol.iterator]|]() {}\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyWorkspaceSymbol(t, [new F.VerifyWorkspaceSymbolCase { Pattern = "iterator", Preferences = null!, Exact = [new LSP.SymbolInformation { Name = "iterator", Kind = LSP.SymbolKind.Method, Location = f.Ranges()[0].LSLocation(), ContainerName = "C" }] }]);
            }
            finally
            {
                done();
            }
        });
    }

}
