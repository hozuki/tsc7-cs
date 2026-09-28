// ported from: internal/fourslash/tests/codeLensOverloads01_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeLensOverloads01Case
{
    public static void TestCodeLensOverloads01(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\nexport function foo(x: number): number;\nexport function foo(x: string): string;\nexport function foo(x: string | number): string | number {\n\treturn x;\n}\n\nfoo(1);\n\nfoo(\"hello\");\n\n// This one isn't expected to match any overload,\n// but is really just here to test how it affects how code lens.\nfoo(Math.random() ? 1 : \"hello\");\n"u8);
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
