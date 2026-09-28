// ported from: internal/fourslash/tests/workspaceSymbolMultiProjectNonExistentRef_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class WorkspaceSymbolMultiProjectNonExistentRefCase
{
    public static void TestWorkspaceSymbolMultiProjectNonExistentRef(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\n// @Filename: /home/src/projects/project-a/tsconfig.json\n{\n  \"compilerOptions\": { \"composite\": true },\n  \"references\": [{ \"path\": \"../project-nonexistent\" }]\n}\n\n// @Filename: /home/src/projects/project-a/index.ts\nexport const [|myValueA|]: number = 1;\n\n// @Filename: /home/src/projects/project-b/tsconfig.json\n{\n  \"compilerOptions\": { \"composite\": true },\n  \"references\": [{ \"path\": \"../project-a\" }]\n}\n\n// @Filename: /home/src/projects/project-b/index.ts\nexport const [|myValueB|]: string = \"hello\";\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyWorkspaceSymbol(t, [new F.VerifyWorkspaceSymbolCase { Pattern = "myValue", Includes = [new LSP.SymbolInformation { Name = "myValueA", Kind = LSP.SymbolKind.Variable, Location = f.Ranges()[0].LSLocation() }, new LSP.SymbolInformation { Name = "myValueB", Kind = LSP.SymbolKind.Variable, Location = f.Ranges()[1].LSLocation() }] }]);
            }
            finally
            {
                done();
            }
        });
    }

}
