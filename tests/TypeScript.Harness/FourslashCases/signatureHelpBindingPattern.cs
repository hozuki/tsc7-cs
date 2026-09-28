// ported from: internal/fourslash/tests/signatureHelpBindingPattern_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpBindingPatternCase
{
    public static void TestSignatureHelpBindingPattern(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\n/**\n * @param options An empty object binding pattern.\n */\nfunction emptyObj({}) {}\nemptyObj(/*emptyObj*/)\n\n/**\n * @param items An empty array binding pattern.\n */\nfunction emptyArr([]) {}\nemptyArr(/*emptyArr*/)\n\n/**\n * @param param An object with a and b properties.\n */\nfunction nonEmptyObj({a, b}: {a: number, b: string}) {}\nnonEmptyObj(/*nonEmptyObj*/)\n\n/**\n * @param tuple A tuple with two elements.\n */\nfunction nonEmptyArr([x, y]: [number, string]) {}\nnonEmptyArr(/*nonEmptyArr*/)\n\n/**\n * @param first The first number parameter.\n * @param second An object with a and b properties.\n */\nfunction idLeading(first: number, {a, b}: {a: number, b: string}) {}\nidLeading(123/*idLeading*/, { a: 1, b: 2 }/*bindingTrailing*/)\n\n/**\n * @param first An object with a and b properties.\n * @param last The last number parameter.\n */\nfunction bindingLeading({a, b}: {a: number, b: string}, last: number) {}\nbindingLeading(/*bindingLeading*/{ a: 1, b: 2 }, 123 /*idTrailing*/)\n\n/**\n * @param param1 {Object} The first parameter\n * @param param1.a {number} Comment a\n * @param param1.b {string} Comment b\n * @param param2 {Object} The second parameter\n * @param param2.c {boolean} Comment c\n * @param param2.d {unknown} Comment d\n */\nfunction multipleBindings({ a, b }, { c, d }) {}\nmultipleBindings({ a: 0, b: \"\" }/*firstObjParam*/, { c: true, d: \"\" }/*secondObjParam*/)\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineSignatureHelp(t);
            }
            finally
            {
                done();
            }
        });
    }

}
