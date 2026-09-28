// ported from: internal/fourslash/tests/goToSourceDefinitionPropertyAccessNoDecl_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToSourceDefinitionPropertyAccessNoDeclCase
{
    public static void TestGoToSourcePropertyAccessNoDeclaration(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"main\": \"./index.js\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.d.ts\ntype Keys = \"alpha\" | \"beta\";\nexport declare const config: { [K in Keys]: string };\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.js\nexport const config = { /*targetAlpha*/alpha: \"a\", /*targetBeta*/beta: \"b\" };\n// @Filename: /home/src/workspaces/project/index.ts\nimport { config } from \"pkg\";\nconfig./*accessAlpha*/alpha;\nconfig./*accessBeta*/beta;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToSourceDefinition(t, "accessAlpha", "accessBeta");
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestGoToSourcePropertyAccessDeepChain(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"main\": \"./index.js\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.d.ts\nexport declare const nested: { inner: { value: number } };\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.js\nexport const nested = { inner: { /*targetValue*/value: 42 } };\n// @Filename: /home/src/workspaces/project/index.ts\nimport { nested } from \"pkg\";\nnested.inner./*accessValue*/value;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToSourceDefinition(t, "accessValue");
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestGoToSourcePropertyAccessNamespaceImport(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"main\": \"./index.js\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.d.ts\ntype Keys = \"x\" | \"y\";\nexport declare const coords: { [K in Keys]: number };\n// @Filename: /home/src/workspaces/project/node_modules/pkg/index.js\nexport const coords = { /*targetX*/x: 10, /*targetY*/y: 20 };\n// @Filename: /home/src/workspaces/project/index.ts\nimport { coords } from \"pkg\";\ncoords./*accessX*/x;\ncoords./*accessY*/y;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToSourceDefinition(t, "accessX", "accessY");
            }
            finally
            {
                done();
            }
        });
    }

}
