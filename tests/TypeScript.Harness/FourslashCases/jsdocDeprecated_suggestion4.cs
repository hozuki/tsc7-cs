// ported from: internal/fourslash/tests/jsdocDeprecated_suggestion4_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsdocDeprecated_suggestion4Case
{
    public static void TestJsdocDeprecated_suggestion4(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @jsx: preserve\n// @filename: a.tsx\ninterface Props {\n    /** @deprecated */\n    x: number\n    y: number\n}\nfunction A(props: Props) {\n    return <div>{props.y}</div>\n}\nfunction B() {\n    return <A [|x|]={1} y={1} />\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "a.tsx");
                f.VerifySuggestionDiagnostics(t, [new LSP.Diagnostic { Message = new LSP.StringOrMarkupContent { String = "'x' is deprecated." }, Code = new LSP.IntegerOrString { Integer = (int)(6385) }, Range = f.Ranges()[0].LSRange, Tags = [LSP.DiagnosticTag.Deprecated] }]);
            }
            finally
            {
                done();
            }
        });
    }

}
