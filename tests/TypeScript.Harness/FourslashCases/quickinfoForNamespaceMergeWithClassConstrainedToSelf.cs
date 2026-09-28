// ported from: internal/fourslash/tests/quickinfoForNamespaceMergeWithClassConstrainedToSelf_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickinfoForNamespaceMergeWithClassConstrainedToSelfCase
{
    public static void TestQuickinfoForNamespaceMergeWithClassConstrainedToSelf(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("declare namespace AMap {\n    namespace MassMarks {\n        interface Data {\n            style?: number;\n        }\n    }\n    class MassMarks<D extends MassMarks.Data = MassMarks.Data> {\n        constructor(data: D[] | string);\n        clear(): void;\n    }\n}\n\ninterface MassMarksCustomData extends AMap.MassMarks./*1*/Data {\n    name: string;\n    id: string;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "interface AMap.MassMarks<D extends AMap.MassMarks.Data = AMap.MassMarks.Data>.Data", "");
            }
            finally
            {
                done();
            }
        });
    }

}
