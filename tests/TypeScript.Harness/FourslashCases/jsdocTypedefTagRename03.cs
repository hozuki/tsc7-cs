// ported from: internal/fourslash/tests/jsdocTypedefTagRename03_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsdocTypedefTagRename03Case
{
    public static void TestJsdocTypedefTagRename03(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n// @allowNonTsExtensions: true\n// @Filename: jsDocTypedef_form3.js\n\n/**\n * [|@typedef /*1*/[|{| \"contextRangeIndex\": 0 |}Person|]\n * @type {Object}\n * @property {number} age\n * @property {string} name\n |]*/\n\n/** @type {/*2*/[|Person|]} */\nvar person;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.GoToFile(t, "jsDocTypedef_form3.js");
                f.VerifyBaselineRename(t, null!, U.ToAny(f.GetRangesByText().Get("Person")!));
            }
            finally
            {
                done();
            }
        });
    }

}
