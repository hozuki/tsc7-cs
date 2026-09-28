// ported from: internal/fourslash/tests/importNameCodeFixUMDGlobalReact0_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFixUMDGlobalReact0Case
{
    public static void TestImportNameCodeFixUMDGlobalReact0(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @jsx: react\n// @allowSyntheticDefaultImports: false\n// @module: es2015\n// @moduleResolution: bundler\n// @Filename: /node_modules/@types/react/index.d.ts\nexport = React;\nexport as namespace React;\ndeclare namespace React {\n    export class Component { render(): JSX.Element | null; }\n}\ndeclare global {\n    namespace JSX {\n        interface Element {}\n    }\n}\n// @Filename: /a.tsx\n[|import { Component } from \"react\";\nexport class MyMap extends Component { }\n<MyMap/>;|]\n// @Filename: /b.tsx\n[|import { Component } from \"react\";\n<></>;|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/a.tsx");
                f.VerifyImportFixAtPosition(t, ["import * as React from \"react\";\nimport { Component } from \"react\";\nexport class MyMap extends Component { }\n<MyMap/>;"], null!);
                f.GoToFile(t, "/b.tsx");
                f.VerifyImportFixAtPosition(t, ["import * as React from \"react\";\nimport { Component } from \"react\";\n<></>;"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
