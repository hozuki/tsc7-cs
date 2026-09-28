// ported from: internal/fourslash/tests/jsDocGenerics2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsDocGenerics2Case
{
    public static void TestJsDocGenerics2(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowNonTsExtensions: true\n// @Filename: Foo.js\n/**\n * @param {T[]} arr\n * @param {(function(T):T)} valuator\n * @template T\n */\nfunction SortFilter(arr,valuator)\n{\n    return arr;\n}\nvar a/*1*/ = SortFilter([0, 1, 2], q/*2*/ => q);\nvar b/*3*/ = SortFilter([0, 1, 2], undefined);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "var a: number[]", "");
                f.VerifyQuickInfoAt(t, "2", "(parameter) q: number", "");
                f.VerifyQuickInfoAt(t, "3", "var b: number[]", "");
            }
            finally
            {
                done();
            }
        });
    }

}
