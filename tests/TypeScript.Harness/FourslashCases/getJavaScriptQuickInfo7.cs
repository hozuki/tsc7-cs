// ported from: internal/fourslash/tests/getJavaScriptQuickInfo7_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetJavaScriptQuickInfo7Case
{
    public static void TestGetJavaScriptQuickInfo7(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowNonTsExtensions: true\n// @Filename: file.js\n/**\n * This is a very cool function that is very nice.\n * @returns something\n * @param p anotherthing\n */\nfunction a1(p) {\n\ttry {\n\t\tthrow new Error('x');\n\t} catch (x) { x--; }\n\treturn 23;\n}\n\nx - /**/a1()"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "", "function a1(p: any): number", "This is a very cool function that is very nice.");
            }
            finally
            {
                done();
            }
        });
    }

}
