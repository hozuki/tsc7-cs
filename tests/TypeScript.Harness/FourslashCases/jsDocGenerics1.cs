// ported from: internal/fourslash/tests/jsDocGenerics1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsDocGenerics1Case
{
    public static void TestJsDocGenerics1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowNonTsExtensions: true\n// @Filename: ref.d.ts\nnamespace Thing {\n    export interface Thung {\n        a: number;\n    ]\n]\n// @Filename: Foo.js\n\n/** @type {Array<number>} */\nvar v;\nv[0]./*1*/\n\n/** @type {{x: Array<Array<number>>}} */\nvar w;\nw.x[0][0]./*2*/\n\n/** @type {Array<Thing.Thung>} */\nvar x;\nx[0].a./*3*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, f.Markers(), new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "toFixed", Kind = LSP.CompletionItemKind.Method }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
