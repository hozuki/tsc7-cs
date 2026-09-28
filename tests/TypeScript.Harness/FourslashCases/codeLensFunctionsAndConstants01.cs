// ported from: internal/fourslash/tests/codeLensFunctionsAndConstants01_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeLensFunctionsAndConstants01Case
{
    public static void TestCodeLensFunctionsAndConstants01(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\n// @module: preserve\n\n// @filename: ./exports.ts\n\nlet callCount = 0;\nexport function foo(n: number): void {\n  callCount++;\n  if (n > 0) {\n\tfoo(n - 1);\n  }\n  else {\n    console.log(\"function was called \" + callCount + \" times\");\n  }\n}\n\nfoo(5);\n\nexport const bar = 123;\n\n// @filename: ./importer.ts\nimport { foo, bar } from \"./exports\";\n\nfoo(5);\nconsole.log(bar);\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineCodeLens(t, new LSUP.UserPreferences { CodeLens = new LSUP.CodeLensUserPreferences { ReferencesCodeLensEnabled = TRI.TSTrue, ReferencesCodeLensShowOnAllFunctions = TRI.TSTrue, ImplementationsCodeLensEnabled = TRI.TSTrue, ImplementationsCodeLensShowOnInterfaceMethods = TRI.TSTrue, ImplementationsCodeLensShowOnAllClassMethods = TRI.TSTrue } });
            }
            finally
            {
                done();
            }
        });
    }

}
