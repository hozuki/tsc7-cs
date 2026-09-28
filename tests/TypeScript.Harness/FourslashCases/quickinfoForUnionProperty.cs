// ported from: internal/fourslash/tests/quickinfoForUnionProperty_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickinfoForUnionPropertyCase
{
    public static void TestQuickinfoForUnionProperty(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface One {\n    commonProperty: number;\n    commonFunction(): number;\n}\n\ninterface Two {\n    commonProperty: string\n    commonFunction(): number;\n}\n\nvar /*1*/x : One | Two;\n\nx./*2*/commonProperty;\nx./*3*/commonFunction;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "var x: One | Two", "");
                f.VerifyQuickInfoAt(t, "2", "(property) commonProperty: string | number", "");
                f.VerifyQuickInfoAt(t, "3", "(method) commonFunction(): number", "");
            }
            finally
            {
                done();
            }
        });
    }

}
