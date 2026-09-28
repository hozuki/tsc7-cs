// ported from: internal/fourslash/tests/jsDocTypeTagQuickInfo1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsDocTypeTagQuickInfo1Case
{
    public static void TestJsDocTypeTagQuickInfo1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n// @strict: true\n// @allowJs: true\n// @Filename: jsDocTypeTag1.js\n/** @type {String} */\nvar /*1*/S;\n/** @type {Number} */\nvar /*2*/N;\n/** @type {Boolean} */\nvar /*3*/B;\n/** @type {Void} */\nvar /*4*/V;\n/** @type {Undefined} */\nvar /*5*/U;\n/** @type {Null} */\nvar /*6*/Nl;\n/** @type {Array} */\nvar /*7*/A;\n/** @type {Promise} */\nvar /*8*/P;\n/** @type {Object} */\nvar /*9*/Obj;\n/** @type {Function} */\nvar /*10*/Func;\n/** @type {*} */\nvar /*11*/AnyType;\n/** @type {?} */\nvar /*12*/QType;\n/** @type {String|Number} */\nvar /*13*/SOrN;"u8);
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
