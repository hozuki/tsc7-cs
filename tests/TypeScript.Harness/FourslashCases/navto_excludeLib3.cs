// ported from: internal/fourslash/tests/navto_excludeLib3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class Navto_excludeLib3Case
{
    public static void TestNavto_excludeLib3(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @filename: /index.ts\nfunction [|parseInt|](s: string): number {}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyWorkspaceSymbol(t, [new F.VerifyWorkspaceSymbolCase { Pattern = "parseInt", Preferences = null!, Exact = [new LSP.SymbolInformation { Name = "parseInt", Kind = LSP.SymbolKind.Function, Location = f.Ranges()[0].LSLocation() }] }]);
            }
            finally
            {
                done();
            }
        });
    }

}
