// ported from: internal/fourslash/tests/importFixesGlobalTypingsCache_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportFixesGlobalTypingsCacheCase
{
    public static void TestImportFixesGlobalTypingsCache(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /project/tsconfig.json\n { \"compilerOptions\": { \"allowJs\": true, \"checkJs\": true, \"module\": \"commonjs\" } }\n// @Filename: /home/src/Library/Caches/typescript/node_modules/@types/react-router-dom/package.json\n { \"name\": \"@types/react-router-dom\", \"version\": \"16.8.4\", \"types\": \"index.d.ts\" }\n// @Filename: /home/src/Library/Caches/typescript/node_modules/@types/react-router-dom/index.d.ts\nexport class BrowserRouter {}\n// @Filename: /project/node_modules/react-router-dom/package.json\n { \"name\": \"react-router-dom\", \"version\": \"16.8.4\", \"main\": \"index.js\" }\n// @Filename: /project/node_modules/react-router-dom/index.js\n export const BrowserRouter = () => null;\n// @Filename: /project/index.js\nBrowserRouter/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/project/index.js");
                f.VerifyImportFixAtPosition(t, ["const { BrowserRouter } = require(\"react-router-dom\");\n\nBrowserRouter"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
