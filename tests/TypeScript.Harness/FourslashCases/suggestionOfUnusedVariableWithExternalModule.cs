// ported from: internal/fourslash/tests/suggestionOfUnusedVariableWithExternalModule_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SuggestionOfUnusedVariableWithExternalModuleCase
{
    public static void TestSuggestionOfUnusedVariableWithExternalModule(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("//@allowJs: true\n//@module: commonjs\n// @Filename: /mymodule.js\n(function ([|root|], factory) {\n    module.exports = factory();\n}(this, function () {\n    var [|unusedVar|] = \"something\";\n    return {};\n}));\n// @Filename: /app.js\n//@ts-check\n[|require(\"./mymodule\")|];"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/app.js");
                f.VerifySuggestionDiagnostics(t, [new LSP.Diagnostic { Code = new LSP.IntegerOrString { Integer = (int)(80001) }, Message = new LSP.StringOrMarkupContent { String = "File is a CommonJS module; it may be converted to an ES module." }, Range = f.Ranges()[2].LSRange }]);
                f.GoToFile(t, "/mymodule.js");
                f.VerifySuggestionDiagnostics(t, [new LSP.Diagnostic { Message = new LSP.StringOrMarkupContent { String = "'root' is declared but its value is never read." }, Code = new LSP.IntegerOrString { Integer = (int)(6133) }, Range = f.Ranges()[0].LSRange, Tags = [LSP.DiagnosticTag.Unnecessary] }, new LSP.Diagnostic { Message = new LSP.StringOrMarkupContent { String = "'unusedVar' is declared but its value is never read." }, Code = new LSP.IntegerOrString { Integer = (int)(6133) }, Range = f.Ranges()[1].LSRange, Tags = [LSP.DiagnosticTag.Unnecessary] }]);
            }
            finally
            {
                done();
            }
        });
    }

}
