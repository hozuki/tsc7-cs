// ported from: internal/fourslash/tests/importNameCodeFix_jsxReact17_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFix_jsxReact17Case
{
    public static void TestImportNameCodeFix_jsxReact17(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @jsx: preserve\n// @module: commonjs\n// @Filename: /node_modules/@types/react/index.d.ts\ndeclare namespace React {\n  function createElement(): any;\n}\nexport = React;\nexport as namespace React;\n\ndeclare global {\n  namespace JSX {\n    interface IntrinsicElements {}\n    interface IntrinsicAttributes {}\n  }  \n}\n// @Filename: /component.tsx\nimport \"react\";\nexport declare function Component(): any;\n// @Filename: /index.tsx\n(<Component/**/ />);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifyImportFixAtPosition(t, ["import { Component } from \"./component\";\n\n(<Component />);"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
