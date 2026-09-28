// ported from: internal/fourslash/tests/organizeImportsReactJsxDev_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class OrganizeImportsReactJsxDevCase
{
    public static void TestOrganizeImportsReactJsxDev(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowSyntheticDefaultImports: true\n// @moduleResolution: bundler\n// @noUnusedLocals: true\n// @target: es2018\n// @jsx: react-jsxdev\n// @filename: test.tsx\nimport React from 'react';\nexport default () => <div></div>\n// @filename: node_modules/react/package.json\n{\n    \"name\": \"react\",\n    \"types\": \"index.d.ts\"\n}\n// @filename: node_modules/react/index.d.ts\nexport = React;\ndeclare namespace JSX {\n    interface IntrinsicElements { [x: string]: any; }\n}\ndeclare namespace React {}\n// @filename: node_modules/react/jsx-runtime.d.ts\nimport './';\n// @filename: node_modules/react/jsx-dev-runtime.d.ts\nimport './';"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "test.tsx");
                f.VerifyOrganizeImports(t, "export default () => <div></div>", LSP.CodeActionKind.SourceOrganizeImportsTs, null!);
            }
            finally
            {
                done();
            }
        });
    }

}
