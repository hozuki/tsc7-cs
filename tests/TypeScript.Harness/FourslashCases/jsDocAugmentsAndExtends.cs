// ported from: internal/fourslash/tests/jsDocAugmentsAndExtends_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsDocAugmentsAndExtendsCase
{
    public static void TestJsDocAugmentsAndExtends(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @checkJs: true\n// @Filename: dummy.js\n/**\n * @augments {Thing<number>}\n * [|@extends {Thing<string>}|]\n */\nclass MyStringThing extends Thing {\n    constructor() {\n        super();\n        var x = this.mine;\n        x/**/;\n    }\n}\n// @Filename: declarations.d.ts\ndeclare class Thing<T> {\n    mine: T;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifyQuickInfoIs(t, "(local var) x: number", "");
                f.VerifyNonSuggestionDiagnostics(t, [new LSP.Diagnostic { Message = new LSP.StringOrMarkupContent { String = "Class declarations cannot have more than one '@augments' or '@extends' tag." }, Code = new LSP.IntegerOrString { Integer = (int)(8025) } }]);
            }
            finally
            {
                done();
            }
        });
    }

}
