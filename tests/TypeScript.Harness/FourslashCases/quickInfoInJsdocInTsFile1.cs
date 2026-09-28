// ported from: internal/fourslash/tests/quickInfoInJsdocInTsFile1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoInJsdocInTsFile1Case
{
    public static void TestQuickInfoInJsdocInTsFile1(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/** @type {() => { /*1*/data: string[] }} */\nfunction test(): { data: string[] } {\n  return {\n    data: [],\n  };\n}\n\n/** @returns {{ /*2*/data: string[] }} */\nfunction test2(): { data: string[] } {\n  return {\n    data: [],\n  };\n}\n\n/** @type {{ /*3*/bar: string; }} */\nconst test3 = { bar: '' };\n\ntype SomeObj = { bar: string; };\n/** @type {SomeObj/*4*/} */\nconst test4 = { bar: '' }\n\n/**\n * @param/*5*/ stuff/*6*/ Stuff to do stuff with\n */\nfunction doStuffWithStuff(stuff: { quantity: number }) {}\n\ndeclare const stuff: { quantity: number };\n/** @see {doStuffWithStuff/*7*/} */\nif (stuff.quantity) {}\n\n/** @type {(a/*8*/: string) => void} */\nfunction test2(a: string) {}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "", "");
                f.VerifyQuickInfoAt(t, "2", "", "");
                f.VerifyQuickInfoAt(t, "3", "", "");
                f.VerifyQuickInfoAt(t, "4", "type SomeObj = {\n    bar: string;\n}", "");
                f.VerifyQuickInfoAt(t, "5", "(parameter) stuff: {\n    quantity: number;\n}", "Stuff to do stuff with");
                f.VerifyQuickInfoAt(t, "6", "(parameter) stuff: {\n    quantity: number;\n}", "Stuff to do stuff with");
                f.VerifyQuickInfoAt(t, "7", "function doStuffWithStuff(stuff: {\n    quantity: number;\n}): void", "");
                f.VerifyQuickInfoAt(t, "8", "", "");
            }
            finally
            {
                done();
            }
        });
    }

}
