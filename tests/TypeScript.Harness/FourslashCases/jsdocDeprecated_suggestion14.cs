// ported from: internal/fourslash/tests/jsdocDeprecated_suggestion14_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsdocDeprecated_suggestion14Case
{
    public static void TestJsdocDeprecated_suggestion14(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: esnext\n// @filename: /a.ts\nexport const a = 1;\nexport const b = 1;\n// @filename: /b.ts\nexport {\n    /** @deprecated a is deprecated */\n    a\n} from \"./a\";\n// @filename: /c.ts\nimport { [|a|] } from \"./b\";\n[|a|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/c.ts");
                f.VerifySuggestionDiagnostics(t, [new LSP.Diagnostic { Code = new LSP.IntegerOrString { Integer = (int)(6385) }, Message = new LSP.StringOrMarkupContent { String = "'a' is deprecated." }, Tags = [LSP.DiagnosticTag.Deprecated], Range = f.Ranges()[0].LSRange }, new LSP.Diagnostic { Code = new LSP.IntegerOrString { Integer = (int)(6385) }, Message = new LSP.StringOrMarkupContent { String = "'a' is deprecated." }, Tags = [LSP.DiagnosticTag.Deprecated], Range = f.Ranges()[1].LSRange }]);
            }
            finally
            {
                done();
            }
        });
    }

}
