// ported from: internal/fourslash/tests/codeFixUnusedIdentifier_suggestion_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixUnusedIdentifier_suggestionCase
{
    public static void TestCodeFixUnusedIdentifier_suggestion(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: false\nfunction f([|p|]) {\n    const [|x|] = 0;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifySuggestionDiagnostics(t, [new LSP.Diagnostic { Message = new LSP.StringOrMarkupContent { String = "Parameter 'p' implicitly has an 'any' type, but a better type may be inferred from usage." }, Range = f.Ranges()[0].LSRange, Code = new LSP.IntegerOrString { Integer = (int)(7044) } }, new LSP.Diagnostic { Message = new LSP.StringOrMarkupContent { String = "'p' is declared but its value is never read." }, Range = f.Ranges()[0].LSRange, Code = new LSP.IntegerOrString { Integer = (int)(6133) }, Tags = [LSP.DiagnosticTag.Unnecessary] }, new LSP.Diagnostic { Message = new LSP.StringOrMarkupContent { String = "'x' is declared but its value is never read." }, Range = f.Ranges()[1].LSRange, Code = new LSP.IntegerOrString { Integer = (int)(6133) }, Tags = [LSP.DiagnosticTag.Unnecessary] }]);
                f.VerifyCodeFixAvailable(t, null!);
            }
            finally
            {
                done();
            }
        });
    }

}
