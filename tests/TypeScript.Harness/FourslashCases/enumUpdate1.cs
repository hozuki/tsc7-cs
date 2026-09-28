// ported from: internal/fourslash/tests/enumUpdate1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class EnumUpdate1Case
{
    public static void TestEnumUpdate1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("namespace M {\n\texport enum E {\n\t\tA = 1,\n\t\tB = 2,\n\t\tC = 3,\n\t\t/*1*/\n\t}\n}\nnamespace M {\n\tfunction foo(): M.E {\n\t\treturn M.E.A;\n\t}\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNoErrors(t);
                f.GoToMarker(t, "1");
                f.Insert(t, "D = C << 1,");
                f.VerifyNoErrors(t);
            }
            finally
            {
                done();
            }
        });
    }

}
