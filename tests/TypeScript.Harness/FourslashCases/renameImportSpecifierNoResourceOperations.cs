// ported from: internal/fourslash/tests/renameImportSpecifierNoResourceOperations_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameImportSpecifierNoResourceOperationsCase
{
    public static void TestRenameImportSpecifierNoResourceOperations(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\n// @Filename: /a.ts\nexport const x = 0;\n// @Filename: /b.ts\nimport * as a from \".//*rename*/a\";"u8);
            var capabilities = F.FourslashTest.GetDefaultCapabilities();
            capabilities!.Workspace!.WorkspaceEdit = new LSP.WorkspaceEditClientCapabilities { DocumentChanges = true, ResourceOperations = [] };
            var (f, done) = F.Fourslash.NewFourslash(t, capabilities, content);
            try
            {
                f.GoToMarker(t, "rename");
                f.VerifyRenameFailed(t, null!);
            }
            finally
            {
                done();
            }
        });
    }

}
