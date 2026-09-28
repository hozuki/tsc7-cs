// ported from: internal/fourslash/tests/jsdocDeprecated_suggestion20_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsdocDeprecated_suggestion20Case
{
    public static void TestJsdocDeprecated_suggestion20(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: esnext\n// @filename: /a.ts\nexport default function a() {}\n// @filename: /b.ts\nimport _a from \"./a\";\nexport {\n\t/** @deprecated a is deprecated */\n\t_a as a,\n};\n/** @deprecated b is deprecated */\nexport const b = (): void => {};\n// @filename: /c.ts\nimport * as _ from \"./b\";\n\n_.[|a|]()\n_.[|b|]()"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/c.ts");
                f.VerifySuggestionDiagnostics(t, [new LSP.Diagnostic { Code = new LSP.IntegerOrString { Integer = (int)(6385) }, Message = new LSP.StringOrMarkupContent { String = "'a' is deprecated." }, Tags = [LSP.DiagnosticTag.Deprecated], Range = f.Ranges()[0].LSRange }, new LSP.Diagnostic { Code = new LSP.IntegerOrString { Integer = (int)(6385) }, Message = new LSP.StringOrMarkupContent { String = "'b' is deprecated." }, Tags = [LSP.DiagnosticTag.Deprecated], Range = f.Ranges()[1].LSRange }]);
            }
            finally
            {
                done();
            }
        });
    }

}
