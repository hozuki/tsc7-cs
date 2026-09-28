// ported from: internal/fourslash/tests/quickInfoOnPropertyAccessInWriteLocation5_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoOnPropertyAccessInWriteLocation5Case
{
    public static void TestQuickInfoOnPropertyAccessInWriteLocation5(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: true\ninterface Serializer {\n  set value(v: string | number);\n  get value(): string;\n}\ndeclare let box: Serializer;\nbox.value/*1*/ += 10;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(property) Serializer.value: string | number", "");
            }
            finally
            {
                done();
            }
        });
    }

}
