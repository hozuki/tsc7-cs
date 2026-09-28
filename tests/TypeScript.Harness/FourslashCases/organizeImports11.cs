// ported from: internal/fourslash/tests/organizeImports11_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class OrganizeImports11Case
{
    public static void TestOrganizeImports11(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /test.ts\nimport { TypeA, TypeB, TypeC, UnreferencedType } from './my-types';\n\n/**\n * MyClass {@link TypeA}\n */\nexport class MyClass {\n\n  /**\n   * Some Property {@link TypeB}\n   */\n  public something;\n\n  /**\n   * Some function {@link TypeC}\n   */\n  public myMethod() {\n\n    /**\n     * Some lambda function {@link TypeC}\n     */\n    const someFunction = () => {\n      return '';\n    }\n    someFunction();\n  }\n}\n// @Filename: /my-types.ts\n export type TypeA = string;\n export class TypeB { }\n export type TypeC = () => string;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "import { TypeA, TypeB, TypeC } from './my-types';\n\n/**\n * MyClass {@link TypeA}\n */\nexport class MyClass {\n\n  /**\n   * Some Property {@link TypeB}\n   */\n  public something;\n\n  /**\n   * Some function {@link TypeC}\n   */\n  public myMethod() {\n\n    /**\n     * Some lambda function {@link TypeC}\n     */\n    const someFunction = () => {\n      return '';\n    }\n    someFunction();\n  }\n}", LSP.CodeActionKind.SourceOrganizeImportsTs, null!);
            }
            finally
            {
                done();
            }
        });
    }

}
