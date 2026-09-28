// ported from: internal/fourslash/tests/quickInfoOnMergedInterfaces_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoOnMergedInterfacesCase
{
    public static void TestQuickInfoOnMergedInterfaces(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("namespace M {\n    interface A<T> {\n        (): string;\n        (x: T): T;\n    }\n    interface A<T> {\n        (x: T, y: number): T;\n        <U>(x: U, y: T): U;\n    }\n    var a: A<boolean>;\n    var r = a();\n    var r2 = a(true);\n    var r3 = a(true, 2);\n    var /*1*/r4 = a(1, true);\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "var r4: number", "");
            }
            finally
            {
                done();
            }
        });
    }

}
