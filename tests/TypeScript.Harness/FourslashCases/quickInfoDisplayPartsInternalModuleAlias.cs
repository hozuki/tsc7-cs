// ported from: internal/fourslash/tests/quickInfoDisplayPartsInternalModuleAlias_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoDisplayPartsInternalModuleAliasCase
{
    public static void TestQuickInfoDisplayPartsInternalModuleAlias(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("namespace m.m1 {\n    export class c {\n    }\n}\nnamespace m2 {\n    import /*1*/a1 = m;\n    new /*2*/a1.m1.c();\n    import /*3*/a2 = m.m1;\n    new /*4*/a2.c();\n    export import /*5*/a3 = m;\n    new /*6*/a3.m1.c();\n    export import /*7*/a4 = m.m1;\n    new /*8*/a4.c();\n}"u8);
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
