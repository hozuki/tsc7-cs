// ported from: internal/fourslash/tests/suggestionNoDuplicates_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SuggestionNoDuplicatesCase
{
    public static void TestSuggestionNoDuplicates(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: false\n// @Filename: foo.ts\nimport { f } from [|'m'|]\nf\n// @Filename: node_modules/m/index.js\nmodule.exports.f = function (x) { return x }"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNonSuggestionDiagnostics(t, null!);
                f.VerifySuggestionDiagnostics(t, [new LSP.Diagnostic { Code = new LSP.IntegerOrString { Integer = (int)(7016) }, Message = new LSP.StringOrMarkupContent { String = "Could not find a declaration file for module 'm'. '/node_modules/m/index.js' implicitly has an 'any' type." } }]);
            }
            finally
            {
                done();
            }
        });
    }

}
