// ported from: internal/fourslash/tests/getSemanticDiagnosticForDeclaration_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetSemanticDiagnosticForDeclarationCase
{
    public static void TestGetSemanticDiagnosticForDeclaration(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: false\n// @module: CommonJS\n// @declaration: true\nexport function /*1*/foo/*2*/() {\n    interface privateInterface {}\n    class Bar implements privateInterface { private a; }\n    return Bar;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyErrorExistsBetweenMarkers(t, "1", "2");
                f.VerifyNumberOfErrorsInCurrentFile(t, 1);
            }
            finally
            {
                done();
            }
        });
    }

}
