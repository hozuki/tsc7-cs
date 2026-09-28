// ported from: internal/fourslash/tests/completionsDotInArrayLiteralInObjectLiteral_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsDotInArrayLiteralInObjectLiteralCase
{
    public static void TestCompletionsDotInArrayLiteralInObjectLiteral(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("const o = { x: [[|.|][||]/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNonSuggestionDiagnostics(t, [new LSP.Diagnostic { Code = new LSP.IntegerOrString { Integer = (int)(1109) }, Message = new LSP.StringOrMarkupContent { String = "Expression expected." }, Range = f.Ranges()[0].LSRange }, new LSP.Diagnostic { Code = new LSP.IntegerOrString { Integer = (int)(1003) }, Message = new LSP.StringOrMarkupContent { String = "Identifier expected." }, Range = f.Ranges()[1].LSRange }]);
                f.VerifyCompletions(t, "", null!);
            }
            finally
            {
                done();
            }
        });
    }

}
