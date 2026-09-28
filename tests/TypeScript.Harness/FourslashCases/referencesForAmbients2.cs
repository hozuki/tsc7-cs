// ported from: internal/fourslash/tests/referencesForAmbients2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ReferencesForAmbients2Case
{
    public static void TestReferencesForAmbients2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /defA.ts\ndeclare module \"a\" {\n    /*1*/export type /*2*/T = number;\n}\n// @Filename: /defB.ts\ndeclare module \"b\" {\n    export import a = require(\"a\");\n    export const x: a./*3*/T;\n}\n// @Filename: /defC.ts\ndeclare module \"c\" {\n    import b = require(\"b\");\n    const x: b.a./*4*/T;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNoErrors(t);
                f.VerifyBaselineFindAllReferences(t, "1", "2", "3", "4");
            }
            finally
            {
                done();
            }
        });
    }

}
