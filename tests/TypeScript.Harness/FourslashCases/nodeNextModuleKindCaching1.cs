// ported from: internal/fourslash/tests/nodeNextModuleKindCaching1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class NodeNextModuleKindCaching1Case
{
    public static void TestNodeNextModuleKindCaching1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: tsconfig.json\n{\n    \"compilerOptions\": {\n      \"lib\": [\"es5\"],\n      \"rootDir\": \"src\",\n      \"outDir\": \"dist\",\n      \"target\": \"ES2020\",\n      \"module\": \"NodeNext\",\n      \"strict\": true\n    },\n    \"include\": [\"src\\\\**\\\\*.ts\"]\n}\n// @Filename: package.json\n{\n    \"type\": \"module\",\n    \"private\": true\n}\n// @Filename: src/index.ts\n// The line below should show a \"Relative import paths need explicit file\n// extensions...\" error in VS Code, but it doesn't. The error is only picked up\n// by `tsc` which seems to properly infer the module type.\nimport { helloWorld } from './example'\n/**/\nhelloWorld()\n// @Filename: src/example.ts\nexport function helloWorld() {\n    console.log('Hello, world!')\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.GoToMarker(t, "");
                f.VerifyNumberOfErrorsInCurrentFile(t, 1);
            }
            finally
            {
                done();
            }
        });
    }

}
