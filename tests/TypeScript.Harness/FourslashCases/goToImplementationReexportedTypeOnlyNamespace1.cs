// ported from: internal/fourslash/tests/goToImplementationReexportedTypeOnlyNamespace1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToImplementationReexportedTypeOnlyNamespace1Case
{
    public static void TestGoToImplementationReexportedTypeOnlyNamespace1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\n// @Filename: /node_modules/@typescript-eslint/types/index.d.ts\nexport * as TSESTree from './generated/ast-spec';\n\n// @Filename: /node_modules/@typescript-eslint/types/generated/ast-spec.d.ts\nexport interface BaseNode {}\n\n// @Filename: /node_modules/@typescript-eslint/utils/index.d.ts\nexport { TSESTree } from '@typescript-eslint/types';\n\n// @Filename: /src/check-license.ts\nimport type {TSE/*impl*/STree} from '@typescript-eslint/utils';\n\nlet node: TSESTree.Node | undefined;\nexport default node;\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToImplementation(t, "impl");
            }
            finally
            {
                done();
            }
        });
    }

}
