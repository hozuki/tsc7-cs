// ported from: internal/fourslash/tests/jsDocTypeTagQuickInfo2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsDocTypeTagQuickInfo2Case
{
    public static void TestJsDocTypeTagQuickInfo2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n// @strict: true\n// @allowJs: true\n// @Filename: jsDocTypeTag2.js\n/** @type {string} */\nvar /*1*/s;\n/** @type {number} */\nvar /*2*/n;\n/** @type {boolean} */\nvar /*3*/b;\n/** @type {void} */\nvar /*4*/v;\n/** @type {undefined} */\nvar /*5*/u;\n/** @type {null} */\nvar /*6*/nl;\n/** @type {array} */\nvar /*7*/a;\n/** @type {promise} */\nvar /*8*/p;\n/** @type {?number} */\nvar /*9*/nullable;\n/** @type {function} */\nvar /*10*/func;\n/** @type {function (number): number} */\nvar /*11*/func1;\n/** @type {string | number} */\nvar /*12*/sOrn;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHover(t);
            }
            finally
            {
                done();
            }
        });
    }

}
