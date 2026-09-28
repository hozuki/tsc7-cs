// ported from: internal/fourslash/tests/jsdocCallbackTag_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsdocCallbackTagCase
{
    public static void TestJsdocCallbackTag(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n// @strict: false\n// @allowNonTsExtensions: true\n// @Filename: jsdocCallbackTag.js\n/**\n * @callback FooHandler - A kind of magic\n * @param {string} eventName - So many words\n * @param eventName2 {number | string} - Silence is golden\n * @param eventName3 - Osterreich mos def\n * @return {number} - DIVEKICK\n */\n/**\n * @type {FooHa/*8*/ndler} callback\n */\nvar t/*1*/;\n\n/**\n * @callback FooHandler2 - What, another one?\n * @param {string=} eventName - it keeps happening\n * @param {string} [eventName2] - i WARNED you dog\n */\n/**\n * @type {FooH/*3*/andler2} callback\n */\nvar t2/*2*/;\nt(/*4*/\"!\", /*5*/12, /*6*/false);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.GoToMarker(t, "1");
                f.VerifyQuickInfoIs(t, "var t: FooHandler", "");
                f.GoToMarker(t, "2");
                f.VerifyQuickInfoIs(t, "var t2: FooHandler2", "");
                f.GoToMarker(t, "3");
                f.VerifyQuickInfoIs(t, "type FooHandler2 = (eventName?: string | undefined, eventName2?: string) => any", "- What, another one?");
                f.GoToMarker(t, "8");
                f.VerifyQuickInfoIs(t, "type FooHandler = (eventName: string, eventName2: number | string, eventName3: any) => number", "- A kind of magic");
            }
            finally
            {
                done();
            }
        });
    }

}
