// ported from: internal/fourslash/tests/jsdocTypedefTagRename04_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsdocTypedefTagRename04Case
{
    public static void TestJsdocTypedefTagRename04(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n// @allowNonTsExtensions: true\n// @Filename: jsDocTypedef_form2.js\n\nfunction test1() {\n   /** @typedef {(string | number)} NumberLike */\n\n   /** @type {/*1*/NumberLike} */\n   var numberLike;\n}\nfunction test2() {\n   /** @typedef {(string | number)} NumberLike2 */\n\n   /** @type {NumberLike2} */\n   var n/*2*/umberLike2;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.GoToMarker(t, "2");
                f.VerifyQuickInfoExists(t);
                f.GoToMarker(t, "1");
                f.Insert(t, "111");
                f.GoToMarker(t, "2");
                f.VerifyQuickInfoExists(t);
            }
            finally
            {
                done();
            }
        });
    }

}
