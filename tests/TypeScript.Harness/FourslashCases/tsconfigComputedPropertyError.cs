// ported from: internal/fourslash/tests/tsconfigComputedPropertyError_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class TsconfigComputedPropertyErrorCase
{
    public static void TestTsconfigComputedPropertyError(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @filename: tsconfig.json\n{\n    [|[\"oops!\" + 42]|]: \"true\",\n    \"compilerOptions\": { \"lib\": [\"es5\"] },\n    \"files\": [\n        \"nonexistentfile.ts\"\n    ],\n    \"compileOnSave\": true\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.VerifyNonSuggestionDiagnostics(t, [new LSP.Diagnostic { Message = new LSP.StringOrMarkupContent { String = "String literal with double quotes expected." }, Code = new LSP.IntegerOrString { Integer = (int)(1327) } }]);
            }
            finally
            {
                done();
            }
        });
    }

}
