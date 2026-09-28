// ported from: internal/fourslash/tests/quickInfoMappedType_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoMappedTypeCase
{
    public static void TestQuickInfoMappedType(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface I {\n  /** m documentation */ m(): void;\n}\ndeclare const o: { [K in keyof I]: number };\no.m/*0*/;\n\ndeclare const p: { [K in keyof I]: I[K] };\np.m/*1*/;\n\ndeclare const q: Pick<I, \"m\">;\nq.m/*2*/;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "0", "(property) m: number", "m documentation");
                f.VerifyQuickInfoAt(t, "1", "(method) m(): void", "m documentation");
                f.VerifyQuickInfoAt(t, "2", "(method) m(): void", "m documentation");
            }
            finally
            {
                done();
            }
        });
    }

}
