// ported from: internal/fourslash/tests/parserCorruptionAfterMapInClass_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ParserCorruptionAfterMapInClassCase
{
    public static void TestParserCorruptionAfterMapInClass(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @target: esnext\n// @lib: es2015\n// @strict: true\nclass C {\n    map = new Set<[|string, number|]>/*$*/\n\n    foo() {\n\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "$");
                f.Insert(t, "()");
                f.VerifyNonSuggestionDiagnostics(t, [new LSP.Diagnostic { Code = new LSP.IntegerOrString { Integer = (int)(2558) }, Message = new LSP.StringOrMarkupContent { String = "Expected 1 type arguments, but got 2." } }]);
            }
            finally
            {
                done();
            }
        });
    }

}
