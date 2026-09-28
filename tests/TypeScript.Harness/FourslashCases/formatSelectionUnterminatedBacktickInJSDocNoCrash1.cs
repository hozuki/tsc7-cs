// ported from: internal/fourslash/tests/formatSelectionUnterminatedBacktickInJSDocNoCrash1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormatSelectionUnterminatedBacktickInJSDocNoCrash1Case
{
    public static void TestFormatSelectionUnterminatedBacktickInJSDocNoCrash1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @filename: index.js\nexport class Manifest {\n  /**\n   * @template {ExtensionType} ExtType\n   * @param {ExtType} extType - `dri\n/*begin*/   * @param {string} extName\n   */\n  setExtension(extType, extName, extData) {\n        const data = _.cloneDeep(extData);\n    this.#data[`${extType}s`][extName] = data;\n    return data;\n  }\n/*end*/\n}\n\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatSelection(t, "begin", "end");
                f.VerifyCurrentFileContent(t, "export class Manifest {\n" + "  /**\n" + "   * @template {ExtensionType} ExtType\n" + "   * @param {ExtType} extType - `dri\n" + "   * @param {string} extName\n" + "   */\n" + "    setExtension(extType, extName, extData) {\n" + "        const data = _.cloneDeep(extData);\n" + "        this.#data[`${extType}s`][extName] = data;\n" + "        return data;\n" + "    }\n" + "\n" + "}\n" + "\n");
            }
            finally
            {
                done();
            }
        });
    }

}
