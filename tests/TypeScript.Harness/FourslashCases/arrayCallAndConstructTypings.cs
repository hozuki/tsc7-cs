// ported from: internal/fourslash/tests/arrayCallAndConstructTypings_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ArrayCallAndConstructTypingsCase
{
    public static void TestArrayCallAndConstructTypings(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("var a/*1*/1 = new Array();\nvar a/*2*/2 = new Array(1);\nvar a/*3*/3 = new Array<boolean>();\nvar a/*4*/4 = new Array<boolean>(1);\nvar a/*5*/5 = new Array(\"s\");\nvar a/*6*/6 = Array();\nvar a/*7*/7 = Array(1);\nvar a/*8*/8 = Array<boolean>();\nvar a/*9*/9 = Array<boolean>(1);\nvar a/*10*/10 = Array(\"s\");"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "var a1: any[]", "");
                f.VerifyQuickInfoAt(t, "2", "var a2: any[]", "");
                f.VerifyQuickInfoAt(t, "3", "var a3: boolean[]", "");
                f.VerifyQuickInfoAt(t, "4", "var a4: boolean[]", "");
                f.VerifyQuickInfoAt(t, "5", "var a5: string[]", "");
                f.VerifyQuickInfoAt(t, "6", "var a6: any[]", "");
                f.VerifyQuickInfoAt(t, "7", "var a7: any[]", "");
                f.VerifyQuickInfoAt(t, "8", "var a8: boolean[]", "");
                f.VerifyQuickInfoAt(t, "9", "var a9: boolean[]", "");
                f.VerifyQuickInfoAt(t, "10", "var a10: string[]", "");
            }
            finally
            {
                done();
            }
        });
    }

}
