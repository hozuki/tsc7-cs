// ported from: internal/fourslash/tests/quickInfoForOverloadOnConst1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoForOverloadOnConst1Case
{
    public static void TestQuickInfoForOverloadOnConst1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface I {\n    x/*1*/1(a: number, callback: (x: 'hi') => number);\n}\nclass C {\n    x/*2*/1(a: number, call/*3*/back: (x: 'hi') => number);\n    x/*4*/1(a: number, call/*5*/back: (x: string) => number) {\n        call/*6*/back('hi');\n        callback('bye');\n        var hm = \"hm\";\n        callback(hm);\n    }\n}\nvar c: C;\nc.x/*7*/1(1, (x/*8*/x: 'hi') => { return 1; } );\nc.x1(1, (x/*9*/x: 'bye') => { return 1; } );\nc.x1(1, (x/*10*/x) => { return 1; } );"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(method) I.x1(a: number, callback: (x: 'hi') => number): any", "");
                f.VerifyQuickInfoAt(t, "2", "(method) C.x1(a: number, callback: (x: 'hi') => number): any", "");
                f.VerifyQuickInfoAt(t, "3", "(parameter) callback: (x: 'hi') => number", "");
                f.VerifyQuickInfoAt(t, "4", "(method) C.x1(a: number, callback: (x: string) => number): void", "");
                f.VerifyQuickInfoAt(t, "5", "(parameter) callback: (x: string) => number", "");
                f.VerifyQuickInfoAt(t, "6", "(parameter) callback: (x: string) => number", "");
                f.VerifyQuickInfoAt(t, "7", "(method) C.x1(a: number, callback: (x: 'hi') => number): any", "");
                f.VerifyQuickInfoAt(t, "8", "(parameter) xx: \"hi\"", "");
                f.VerifyQuickInfoAt(t, "9", "(parameter) xx: \"bye\"", "");
                f.VerifyQuickInfoAt(t, "10", "(parameter) xx: \"hi\"", "");
            }
            finally
            {
                done();
            }
        });
    }

}
