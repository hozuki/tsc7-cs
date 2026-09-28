// ported from: internal/fourslash/tests/quickInfoJSDocTypedefPropertyWithInvalidTag_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoJSDocTypedefPropertyWithInvalidTagCase
{
    public static void TestQuickInfoJSDocTypedefPropertyWithInvalidTag(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: /a.js\n/**\n * @typedef {Object} MyType1\n * @property {string} name\n * @-rule\n * @property {number} age\n */\n\n/**\n * @typedef {Object} MyType2\n * @property {string} name\n * some comment\n * @property {number} age\n */\n\n/**\n * @typedef {Object} MyType3\n * @property {string} name\n * @*stars\n * @property {number} age\n */\n\n/**\n * @typedef {Object} MyType4\n * @property {string} name\n * @(parens)\n * @property {number} age\n */\n\n/**\n * @typedef {Object} MyType5\n * @property {string} name\n * @foo*bar\n * @property {number} age\n */\n\n/** @type {/*t1*/MyType1} */\nconst obj1 = { /*1n*/name: \"\", /*1a*/age: 10 };\n\n/** @type {/*t2*/MyType2} */\nconst obj2 = { /*2n*/name: \"\", /*2a*/age: 10 };\n\n/** @type {/*t3*/MyType3} */\nconst obj3 = { /*3n*/name: \"\", /*3a*/age: 10 };\n\n/** @type {/*t4*/MyType4} */\nconst obj4 = { /*4n*/name: \"\", /*4a*/age: 10 };\n\n/** @type {/*t5*/MyType5} */\nconst obj5 = { /*5n*/name: \"\", /*5a*/age: 10 };\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "t1", "type MyType1 = {\n    name: string;\n    age: number;\n}", "");
                f.VerifyQuickInfoAt(t, "t2", "type MyType2 = {\n    name: string;\n    age: number;\n}", "");
                f.VerifyQuickInfoAt(t, "t3", "type MyType3 = {\n    name: string;\n    age: number;\n}", "");
                f.VerifyQuickInfoAt(t, "t4", "type MyType4 = {\n    name: string;\n    age: number;\n}", "");
                f.VerifyQuickInfoAt(t, "t5", "type MyType5 = {\n    name: string;\n}", "" + "\n\n*@foo* \u2014 *bar" + "\n\n*@property* \u2014 {number} age");
                f.VerifyQuickInfoAt(t, "1n", "(property) name: string", "@-rule");
                f.VerifyQuickInfoAt(t, "2n", "(property) name: string", "some comment");
                f.VerifyQuickInfoAt(t, "3n", "(property) name: string", "@*stars");
                f.VerifyQuickInfoAt(t, "4n", "(property) name: string", "@(parens)");
                f.VerifyQuickInfoAt(t, "5n", "(property) name: string", "");
                f.VerifyQuickInfoAt(t, "1a", "(property) age: number", "");
                f.VerifyQuickInfoAt(t, "2a", "(property) age: number", "");
                f.VerifyQuickInfoAt(t, "3a", "(property) age: number", "");
                f.VerifyQuickInfoAt(t, "4a", "(property) age: number", "");
                f.VerifyQuickInfoAt(t, "5a", "(property) age: number", "");
            }
            finally
            {
                done();
            }
        });
    }

}
