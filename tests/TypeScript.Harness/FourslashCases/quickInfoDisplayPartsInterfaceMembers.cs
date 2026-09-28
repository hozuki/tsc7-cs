// ported from: internal/fourslash/tests/quickInfoDisplayPartsInterfaceMembers_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoDisplayPartsInterfaceMembersCase
{
    public static void TestQuickInfoDisplayPartsInterfaceMembers(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface I {\n    /*1*/property: string;\n    /*2*/method(): string;\n    (): string;\n    new (): I;\n}\nvar iInstance: I;\n/*3*/iInstance./*4*/property = /*5*/iInstance./*6*/method();\n/*7*/iInstance();\nvar /*8*/anotherInstance = new /*9*/iInstance();"u8);
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
