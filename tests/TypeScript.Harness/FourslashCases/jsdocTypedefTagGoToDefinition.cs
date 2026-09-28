// ported from: internal/fourslash/tests/jsdocTypedefTagGoToDefinition_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsdocTypedefTagGoToDefinitionCase
{
    public static void TestJsdocTypedefTagGoToDefinition(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n// @allowNonTsExtensions: true\n// @Filename: jsdocCompletion_typedef.js\n/**\n * @typedef {Object} Person\n * @property {string} /*1*/personName\n * @property {number} personAge\n */\n\n/**\n * @typedef {{ /*2*/animalName: string, animalAge: number }} Animal\n */\n\n/** @type {Person} */\nvar person; person.[|personName/*3*/|]\n\n/** @type {Animal} */\nvar animal; animal.[|animalName/*4*/|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.VerifyBaselineGoToDefinition(t, true, "3", "4");
            }
            finally
            {
                done();
            }
        });
    }

}
