// ported from: internal/fourslash/tests/jsdocDeprecated_suggestion2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsdocDeprecated_suggestion2Case
{
    public static void TestJsdocDeprecated_suggestion2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// overloads\ndeclare function foo(a: string): number;\n/** @deprecated */\ndeclare function foo(): undefined;\ndeclare function foo (a?: string): number | undefined;\n[|foo|]();\nfoo('');\nfoo;\n/** @deprecated */\ndeclare function bar(): number;\n[|bar|]();\n[|bar|];\n/** @deprecated */\ndeclare function baz(): number;\n/** @deprecated */\ndeclare function baz(): number | undefined;\n[|baz|]();\n[|baz|];\ninterface Foo {\n    /** @deprecated */\n    (): void\n    (a: number): void\n}\ndeclare const f: Foo;\n[|f|]();\nf(1);\ninterface T {\n    createElement(): void\n    /** @deprecated */\n    createElement(tag: 'xmp'): void;\n}\ndeclare const t: T;\nt.createElement();\nt.[|createElement|]('xmp');\ndeclare class C {\n    /** @deprecated */\n    constructor ();\n    constructor(v: string)\n}\nC;\nconst c = new [|C|]();\ninterface Ca {\n    /** @deprecated */\n    (): void\n    new (): void\n}\ninterface Cb {\n    (): void\n    /** @deprecated */\n    new (): string\n}\ndeclare const ca: Ca;\ndeclare const cb: Cb;\nca;\ncb;\n[|ca|]();\ncb();\nnew ca();\nnew [|cb|]();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifySuggestionDiagnostics(t, [new LSP.Diagnostic { Message = new LSP.StringOrMarkupContent { String = "The signature '(): undefined' of 'foo' is deprecated." }, Code = new LSP.IntegerOrString { Integer = (int)(6387) }, Range = f.Ranges()[0].LSRange, Tags = [LSP.DiagnosticTag.Deprecated] }, new LSP.Diagnostic { Message = new LSP.StringOrMarkupContent { String = "The signature '(): number' of 'bar' is deprecated." }, Code = new LSP.IntegerOrString { Integer = (int)(6387) }, Range = f.Ranges()[1].LSRange, Tags = [LSP.DiagnosticTag.Deprecated] }, new LSP.Diagnostic { Message = new LSP.StringOrMarkupContent { String = "'bar' is deprecated." }, Code = new LSP.IntegerOrString { Integer = (int)(6385) }, Range = f.Ranges()[2].LSRange, Tags = [LSP.DiagnosticTag.Deprecated] }, new LSP.Diagnostic { Message = new LSP.StringOrMarkupContent { String = "The signature '(): number' of 'baz' is deprecated." }, Code = new LSP.IntegerOrString { Integer = (int)(6387) }, Range = f.Ranges()[3].LSRange, Tags = [LSP.DiagnosticTag.Deprecated] }, new LSP.Diagnostic { Message = new LSP.StringOrMarkupContent { String = "'baz' is deprecated." }, Code = new LSP.IntegerOrString { Integer = (int)(6385) }, Range = f.Ranges()[4].LSRange, Tags = [LSP.DiagnosticTag.Deprecated] }, new LSP.Diagnostic { Message = new LSP.StringOrMarkupContent { String = "The signature '(): void' of 'f' is deprecated." }, Code = new LSP.IntegerOrString { Integer = (int)(6387) }, Range = f.Ranges()[5].LSRange, Tags = [LSP.DiagnosticTag.Deprecated] }, new LSP.Diagnostic { Message = new LSP.StringOrMarkupContent { String = "The signature '(tag: \"xmp\"): void' of 't.createElement' is deprecated." }, Code = new LSP.IntegerOrString { Integer = (int)(6387) }, Range = f.Ranges()[6].LSRange, Tags = [LSP.DiagnosticTag.Deprecated] }, new LSP.Diagnostic { Message = new LSP.StringOrMarkupContent { String = "The signature 'new (): C' of 'C' is deprecated." }, Code = new LSP.IntegerOrString { Integer = (int)(6387) }, Range = f.Ranges()[7].LSRange, Tags = [LSP.DiagnosticTag.Deprecated] }, new LSP.Diagnostic { Message = new LSP.StringOrMarkupContent { String = "The signature '(): void' of 'ca' is deprecated." }, Code = new LSP.IntegerOrString { Integer = (int)(6387) }, Range = f.Ranges()[8].LSRange, Tags = [LSP.DiagnosticTag.Deprecated] }, new LSP.Diagnostic { Message = new LSP.StringOrMarkupContent { String = "The signature 'new (): string' of 'cb' is deprecated." }, Code = new LSP.IntegerOrString { Integer = (int)(6387) }, Range = f.Ranges()[9].LSRange, Tags = [LSP.DiagnosticTag.Deprecated] }]);
            }
            finally
            {
                done();
            }
        });
    }

}
