// ported from: internal/fourslash/tests/importNameCodeFix_jsx6_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFix_jsx6Case
{
    public static void TestImportNameCodeFix_jsx6(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n// @jsx: react\n// @module: esnext\n// @esModuleInterop: true\n// @moduleResolution: bundler\n// @Filename: /node_modules/react/index.d.ts\nexport = React;\nexport as namespace React;\ndeclare namespace React {\n    class Component {}\n}\n// @Filename: /node_modules/react-native/index.d.ts\nimport * as React from \"react\";\nexport class Text extends React.Component {};\n// @Filename: /a.tsx\n<[|Text|]></Text>;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/a.tsx");
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Add import from \"react-native\"", NewFileContent = "import { Text } from \"react-native\";\n\n<Text></Text>;", Index = 0 });
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Import 'React' from \"react\"", NewFileContent = "import React from \"react\";\n\n<Text></Text>;", Index = 1 });
            }
            finally
            {
                done();
            }
        });
    }

}
