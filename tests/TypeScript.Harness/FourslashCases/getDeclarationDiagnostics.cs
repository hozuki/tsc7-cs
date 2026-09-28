// ported from: internal/fourslash/tests/getDeclarationDiagnostics_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetDeclarationDiagnosticsCase
{
    public static void TestGetDeclarationDiagnostics(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: false\n// @declaration: true\n// @outDir: out\n// @Filename: inputFile1.ts\nnamespace m {\n   export function foo() {\n       class C implements I { private a; }\n       interface I { }\n       return C;\n   }\n} /*1*/\n// @Filename: input2.ts\nvar x = \"hello world\"; /*2*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.VerifyNumberOfErrorsInCurrentFile(t, 1);
                f.GoToMarker(t, "2");
                f.VerifyNumberOfErrorsInCurrentFile(t, 0);
            }
            finally
            {
                done();
            }
        });
    }

}
