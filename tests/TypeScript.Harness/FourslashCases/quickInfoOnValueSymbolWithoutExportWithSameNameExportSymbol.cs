// ported from: internal/fourslash/tests/quickInfoOnValueSymbolWithoutExportWithSameNameExportSymbol_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoOnValueSymbolWithoutExportWithSameNameExportSymbolCase
{
    public static void TestQuickInfoOnValueSymbolWithoutExportWithSameNameExportSymbol(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: true\n\ndeclare function num(): number\nconst /*1*/Unit = num()\nexport type Unit = number\nconst value = /*2*/Unit\n\nfunction Fn() {}\nexport type Fn = () => void\n/*3*/Fn()\n\n// repro from #41897\nconst /*4*/X = 1;\nexport interface X {}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "const Unit: number", "");
                f.VerifyQuickInfoAt(t, "2", "const Unit: number", "");
                f.VerifyQuickInfoAt(t, "3", "function Fn(): void", "");
                f.VerifyQuickInfoAt(t, "4", "const X: 1", "");
            }
            finally
            {
                done();
            }
        });
    }

}
