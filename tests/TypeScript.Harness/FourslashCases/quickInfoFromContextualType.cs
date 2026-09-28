// ported from: internal/fourslash/tests/quickInfoFromContextualType_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoFromContextualTypeCase
{
    public static void TestQuickInfoFromContextualType(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: quickInfoExportAssignmentOfGenericInterface_0.ts\ninterface I {\n    /** Documentation */\n    x: number;\n}\nconst i: I = { /**/x: 0 };"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "", "(property) I.x: number", "Documentation");
            }
            finally
            {
                done();
            }
        });
    }

}
