// ported from: internal/fourslash/tests/jsdocDeprecated_suggestion6_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsdocDeprecated_suggestion6Case
{
    public static void TestJsdocDeprecated_suggestion6(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: a.tsx\n/** @deprecated */\ntype Props = {}\n/** @deprecated */\nconst Component = (props: [|Props|]) => props && <div />;\n<[|Component|] old=\"old\" new=\"new\" />\n/** @deprecated */\ntype Options = {}\n/** @deprecated */\nconst deprecatedFunction = (options: [|Options|]) => { options }\n[|deprecatedFunction|]({});"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "a.tsx");
                f.VerifySuggestionDiagnostics(t, [new LSP.Diagnostic { Message = new LSP.StringOrMarkupContent { String = "'Props' is deprecated." }, Code = new LSP.IntegerOrString { Integer = (int)(6385) }, Range = f.Ranges()[0].LSRange, Tags = [LSP.DiagnosticTag.Deprecated] }, new LSP.Diagnostic { Message = new LSP.StringOrMarkupContent { String = "'Component' is deprecated." }, Code = new LSP.IntegerOrString { Integer = (int)(6385) }, Range = f.Ranges()[1].LSRange, Tags = [LSP.DiagnosticTag.Deprecated] }, new LSP.Diagnostic { Message = new LSP.StringOrMarkupContent { String = "'Options' is deprecated." }, Code = new LSP.IntegerOrString { Integer = (int)(6385) }, Range = f.Ranges()[2].LSRange, Tags = [LSP.DiagnosticTag.Deprecated] }, new LSP.Diagnostic { Message = new LSP.StringOrMarkupContent { String = "'deprecatedFunction' is deprecated." }, Code = new LSP.IntegerOrString { Integer = (int)(6385) }, Range = f.Ranges()[3].LSRange, Tags = [LSP.DiagnosticTag.Deprecated] }]);
            }
            finally
            {
                done();
            }
        });
    }

}
