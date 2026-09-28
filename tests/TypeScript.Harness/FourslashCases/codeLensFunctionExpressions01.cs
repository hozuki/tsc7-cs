// ported from: internal/fourslash/tests/codeLensFunctionExpressions01_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeLensFunctionExpressions01Case
{
    public static void TestCodeLensFunctionExpressions01(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\n// @filename: anonymousFunctionExpressions.ts\nexport let anonFn1 = function () {};\nexport const anonFn2 = function () {};\n\nlet anonFn3 = function () {};\nconst anonFn4 = function () {};\n\n// @filename: arrowFunctions.ts\nexport let arrowFn1 = () => {};\nexport const arrowFn2 = () => {};\n\nlet arrowFn3 = () => {};\nconst arrowFn4 = () => {};\n\n// @filename: namedFunctions.ts\nexport let namedFn1 = function namedFn1() {\n    namedFn1();\n}\nnamedFn1();\n\nexport const namedFn2 = function namedFn2() {\n    namedFn2();\n}\nnamedFn2();\n\nlet namedFn3 = function namedFn3() {};\nconst namedFn4 = function namedFn4() {};\n"u8);
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
