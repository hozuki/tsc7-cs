// ported from: internal/fourslash/tests/quickInfoExtendArray_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoExtendArrayCase
{
    public static void TestQuickInfoExtendArray(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Foo<T> extends Array<T> { }\nvar x: Foo<string>;\nvar /*1*/r = x[0];\ninterface Foo2 extends Array<string> { }\nvar x2: Foo2;\nvar /*2*/r2 = x2[0];"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "var r: string", "");
                f.VerifyQuickInfoAt(t, "2", "var r2: string", "");
            }
            finally
            {
                done();
            }
        });
    }

}
