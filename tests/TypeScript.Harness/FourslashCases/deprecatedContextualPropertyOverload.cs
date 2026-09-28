// ported from: internal/fourslash/tests/deprecatedContextualPropertyOverload_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class DeprecatedContextualPropertyOverloadCase
{
    public static void TestDeprecatedContextualPropertyOverload(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface DeprecatedOptions {\n    kind: \"deprecated\";\n    /** @deprecated */\n    value: number;\n}\ninterface CurrentOptions {\n    kind: \"current\";\n    value: number;\n}\ndeclare function select(options: DeprecatedOptions): void;\ndeclare function select(options: CurrentOptions): void;\n\nselect({ kind: \"current\", value: 1 });\n\n/** @deprecated */\ndeclare const deprecatedValue: number;\nselect({ kind: \"current\", value: [|deprecatedValue|] });\n\ninterface DeprecatedContainer {\n    /** @deprecated */\n    value: number;\n}\ndeclare const deprecatedContainer: DeprecatedContainer;\nselect({ kind: \"current\", value: deprecatedContainer.[|value|] });\n\n/** @deprecated */\ndeclare function deprecatedCall(): number;\nselect({ kind: \"current\", value: [|deprecatedCall|]() });\n\ninterface DeprecatedAccessorOptions {\n    /** @deprecated */\n    value: number;\n}\ndeclare function accessor(options: DeprecatedAccessorOptions): void;\naccessor({ get [|value|]() { return 1; } });\naccessor({ set [|value|](_value: number) {} });\n\ninterface DeprecatedNamedOptions {\n    /** @deprecated */\n    \"string-name\": number;\n    /** @deprecated */\n    1: number;\n}\ndeclare function named(options: DeprecatedNamedOptions): void;\nnamed({ [|\"string-name\"|]: 1, [|1|]: 1 });\n\ninterface FirstDeprecatedOptions {\n    kind: \"first\";\n    /** @deprecated */\n    value: number;\n}\ninterface SecondDeprecatedOptions {\n    kind: \"second\";\n    /** @deprecated */\n    value: number;\n}\ndeclare function selectDeprecated(options: FirstDeprecatedOptions): void;\ndeclare function selectDeprecated(options: SecondDeprecatedOptions): void;\n\nselectDeprecated({ kind: \"second\", [|value|]: 1 });"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifySuggestionDiagnostics(t, [new LSP.Diagnostic { Code = new LSP.IntegerOrString { Integer = (int)(6385) }, Message = new LSP.StringOrMarkupContent { String = "'deprecatedValue' is deprecated." }, Tags = [LSP.DiagnosticTag.Deprecated], Range = f.Ranges()[0].LSRange }, new LSP.Diagnostic { Code = new LSP.IntegerOrString { Integer = (int)(6385) }, Message = new LSP.StringOrMarkupContent { String = "'value' is deprecated." }, Tags = [LSP.DiagnosticTag.Deprecated], Range = f.Ranges()[1].LSRange }, new LSP.Diagnostic { Code = new LSP.IntegerOrString { Integer = (int)(6387) }, Message = new LSP.StringOrMarkupContent { String = "The signature '(): number' of 'deprecatedCall' is deprecated." }, Tags = [LSP.DiagnosticTag.Deprecated], Range = f.Ranges()[2].LSRange }, new LSP.Diagnostic { Code = new LSP.IntegerOrString { Integer = (int)(6385) }, Message = new LSP.StringOrMarkupContent { String = "'value' is deprecated." }, Tags = [LSP.DiagnosticTag.Deprecated], Range = f.Ranges()[3].LSRange }, new LSP.Diagnostic { Code = new LSP.IntegerOrString { Integer = (int)(6385) }, Message = new LSP.StringOrMarkupContent { String = "'value' is deprecated." }, Tags = [LSP.DiagnosticTag.Deprecated], Range = f.Ranges()[4].LSRange }, new LSP.Diagnostic { Code = new LSP.IntegerOrString { Integer = (int)(6385) }, Message = new LSP.StringOrMarkupContent { String = "'string-name' is deprecated." }, Tags = [LSP.DiagnosticTag.Deprecated], Range = f.Ranges()[5].LSRange }, new LSP.Diagnostic { Code = new LSP.IntegerOrString { Integer = (int)(6385) }, Message = new LSP.StringOrMarkupContent { String = "'1' is deprecated." }, Tags = [LSP.DiagnosticTag.Deprecated], Range = f.Ranges()[6].LSRange }, new LSP.Diagnostic { Code = new LSP.IntegerOrString { Integer = (int)(6385) }, Message = new LSP.StringOrMarkupContent { String = "'value' is deprecated." }, Tags = [LSP.DiagnosticTag.Deprecated], Range = f.Ranges()[7].LSRange }]);
            }
            finally
            {
                done();
            }
        });
    }

}
