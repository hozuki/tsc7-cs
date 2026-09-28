// ported from: internal/fourslash/tests/codeFixMissingTypeAnnotationOnExports47_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixMissingTypeAnnotationOnExports47Case
{
    public static void TestCodeFixMissingTypeAnnotationOnExports47(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @isolatedDeclarations: true\n// @declaration: true\n// @moduleResolution: bundler\n// @target: es2018\n// @jsx: react-jsx\n// @filename: node_modules/react/package.json\n{\n    \"name\": \"react\",\n    \"types\": \"index.d.ts\"\n}\n// @filename: node_modules/react/index.d.ts\nexport = React;\ndeclare namespace JSX {\n    interface Element extends GlobalJSXElement { }\n    interface IntrinsicElements extends GlobalJSXIntrinsicElements { }\n}\ndeclare namespace React { }\ndeclare global {\n    namespace JSX {\n        interface Element { }\n        interface IntrinsicElements { [x: string]: any; }\n    }\n}\ninterface GlobalJSXElement extends JSX.Element {}\ninterface GlobalJSXIntrinsicElements extends JSX.IntrinsicElements {}\n// @filename: node_modules/react/jsx-runtime.d.ts\nimport './';\n// @filename: node_modules/react/jsx-dev-runtime.d.ts\nimport './';\n// @filename: /a.tsx\nexport const x = <div aria-label=\"label text\" />;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/a.tsx");
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Add annotation of type 'JSX.Element'", NewFileContent = "export const x: JSX.Element = <div aria-label=\"label text\" />;", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
