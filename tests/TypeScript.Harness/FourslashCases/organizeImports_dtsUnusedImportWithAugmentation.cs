// ported from: internal/fourslash/tests/organizeImports_dtsUnusedImportWithAugmentation_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class OrganizeImports_dtsUnusedImportWithAugmentationCase
{
    public static void TestOrganizeImports_dtsUnusedImportWithAugmentation(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /styled-patch.d.ts\nimport * as styledComponents from 'styled-components';\n\ndeclare module 'styled-components' {\n    interface ThemedStyledComponentsModule {\n        keyframes(): Keyframes;\n    }\n}\n// @Filename: /node_modules/styled-components/index.d.ts\nexport interface Keyframes {}\nexport interface ThemedStyledComponentsModule {}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "import 'styled-components';\n\ndeclare module 'styled-components' {\n    interface ThemedStyledComponentsModule {\n        keyframes(): Keyframes;\n    }\n}", LSP.CodeActionKind.SourceOrganizeImportsTs, null!);
            }
            finally
            {
                done();
            }
        });
    }

}
