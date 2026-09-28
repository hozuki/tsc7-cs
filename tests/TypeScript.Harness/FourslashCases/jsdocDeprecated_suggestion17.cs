// ported from: internal/fourslash/tests/jsdocDeprecated_suggestion17_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsdocDeprecated_suggestion17Case
{
    public static void TestJsdocDeprecated_suggestion17(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @filename: foo.ts\ninterface Foo {\n    /** @deprecated */\n    [k: string]: any;\n    /** @deprecated please use `.y` instead  */\n    x: number;\n    y: number;\n}\nfunction f(foo: Foo) {\n    foo.[|x|];\n    foo.[|y|];\n    foo.[|z|];\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifySuggestionDiagnostics(t, [new LSP.Diagnostic { Code = new LSP.IntegerOrString { Integer = (int)(6385) }, Message = new LSP.StringOrMarkupContent { String = "'x' is deprecated." }, Tags = [LSP.DiagnosticTag.Deprecated], Range = f.Ranges()[0].LSRange }, new LSP.Diagnostic { Code = new LSP.IntegerOrString { Integer = (int)(6385) }, Message = new LSP.StringOrMarkupContent { String = "'z' is deprecated." }, Tags = [LSP.DiagnosticTag.Deprecated], Range = f.Ranges()[2].LSRange }]);
            }
            finally
            {
                done();
            }
        });
    }

}
