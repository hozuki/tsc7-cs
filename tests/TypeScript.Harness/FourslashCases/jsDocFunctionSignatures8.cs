// ported from: internal/fourslash/tests/jsDocFunctionSignatures8_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsDocFunctionSignatures8Case
{
    public static void TestJsDocFunctionSignatures8(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: Foo.js\n/**\n * Represents a person\n * a b multiline test\n * @constructor\n * @param {string} name The name of the person\n * @param {number} age The age of the person\n */\nfunction Person(name, age) {\n    this.name = name;\n    this.age = age;\n}\nvar p = new Pers/**/on();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifyQuickInfoIs(t, "constructor Person(name: string, age: number): Person", "Represents a person\na b multiline test");
            }
            finally
            {
                done();
            }
        });
    }

}
