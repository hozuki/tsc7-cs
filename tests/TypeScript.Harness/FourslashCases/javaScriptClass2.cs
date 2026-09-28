// ported from: internal/fourslash/tests/javaScriptClass2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JavaScriptClass2Case
{
    public static void TestJavaScriptClass2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowNonTsExtensions: true\n// @Filename: Foo.js\nclass Foo {\n   constructor() {\n       [|this.[|{| \"contextRangeIndex\": 0 |}union|] = 'foo';|]\n       [|this.[|{| \"contextRangeIndex\": 2 |}union|] = 100;|]\n   }\n   method() { return this.[|union|]; }\n}\nvar x = new Foo();\nx.[|union|];"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineRenameAtRangesWithText(t, null!, "union");
            }
            finally
            {
                done();
            }
        });
    }

}
