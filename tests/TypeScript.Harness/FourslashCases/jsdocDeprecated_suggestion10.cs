// ported from: internal/fourslash/tests/jsdocDeprecated_suggestion10_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsdocDeprecated_suggestion10Case
{
    public static void TestJsdocDeprecated_suggestion10(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @filename: foo.ts\nexport namespace foo {\n    /** @deprecated */\n    export const bar = 1;\n    [|bar|];\n}\nfoo.[|bar|];\nfoo[[|\"bar\"|]];"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "foo.ts");
                f.VerifySuggestionDiagnostics(t, [new LSP.Diagnostic { Code = new LSP.IntegerOrString { Integer = (int)(6385) }, Message = new LSP.StringOrMarkupContent { String = "'bar' is deprecated." }, Tags = [LSP.DiagnosticTag.Deprecated], Range = f.Ranges()[0].LSRange }, new LSP.Diagnostic { Code = new LSP.IntegerOrString { Integer = (int)(6385) }, Message = new LSP.StringOrMarkupContent { String = "'bar' is deprecated." }, Tags = [LSP.DiagnosticTag.Deprecated], Range = f.Ranges()[1].LSRange }, new LSP.Diagnostic { Code = new LSP.IntegerOrString { Integer = (int)(6385) }, Message = new LSP.StringOrMarkupContent { String = "'bar' is deprecated." }, Tags = [LSP.DiagnosticTag.Deprecated], Range = f.Ranges()[2].LSRange }]);
            }
            finally
            {
                done();
            }
        });
    }

}
