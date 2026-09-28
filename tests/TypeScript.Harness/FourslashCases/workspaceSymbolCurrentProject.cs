// ported from: internal/fourslash/tests/workspaceSymbolCurrentProject_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using LSU = TypeScript.LsUtil.LsUtil;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class WorkspaceSymbolCurrentProjectCase
{
    public static void TestWorkspaceSymbolCurrentProject(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\n// @Filename: /home/projects/a/tsconfig.json\n{}\n\n// @Filename: /home/projects/a/index.ts\nexport function [|fromA|]() {}\n\n// @Filename: /home/projects/b/tsconfig.json\n{}\n\n// @Filename: /home/projects/b/index.ts\nexport function [|fromB|]() {}\n"u8);
            var allOpenProjects = LSU.NewDefaultUserPreferences();
            var currentProject = LSU.NewDefaultUserPreferences();
            currentProject!.WorkspaceSymbolsScope = LSU.WorkspaceSymbolsScopeCurrentProject;
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/home/projects/a/index.ts");
                f.VerifyWorkspaceSymbol(t, [new F.VerifyWorkspaceSymbolCase { Pattern = "from", Preferences = allOpenProjects, Exact = [new LSP.SymbolInformation { Name = "fromA", Kind = LSP.SymbolKind.Function, Location = f.Ranges()[0].LSLocation() }, new LSP.SymbolInformation { Name = "fromB", Kind = LSP.SymbolKind.Function, Location = f.Ranges()[1].LSLocation() }] }, new F.VerifyWorkspaceSymbolCase { Pattern = "from", Preferences = currentProject, Exact = [new LSP.SymbolInformation { Name = "fromA", Kind = LSP.SymbolKind.Function, Location = f.Ranges()[0].LSLocation() }] }]);
            }
            finally
            {
                done();
            }
        });
    }

}
