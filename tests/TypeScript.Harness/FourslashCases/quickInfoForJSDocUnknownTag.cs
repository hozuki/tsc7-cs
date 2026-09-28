// ported from: internal/fourslash/tests/quickInfoForJSDocUnknownTag_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoForJSDocUnknownTagCase
{
    public static void TestQuickInfoForJSDocUnknownTag(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/**\n * @example\n * if (true) {\n *     foo()\n * }\n */\nfunction fo/*1*/o() {\n    return '2';\n}\n/**\n @example\n {\n     foo()\n }\n */\nfunction fo/*2*/o2() {\n    return '2';\n}\n/**\n * @example\n *   x y\n *   12345\n *      b\n */\nfunction m/*3*/oo() {\n    return '2';\n}\n/**\n * @func\n * @example\n *   x y\n *   12345\n *      b\n */\nfunction b/*4*/oo() {\n    return '2';\n}\n/**\n * @func\n * @example    x y\n *             12345\n *                b\n */\nfunction go/*5*/o() {\n    return '2';\n}"u8);
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
