// ported from: internal/fourslash/tests/restArgType_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RestArgTypeCase
{
    public static void TestRestArgType(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class Test {\n    private _priv(.../*1*/restArgs) {\n    }\n    public pub(.../*2*/restArgs) {\n        var x = restArgs[2];\n    }\n}\nvar x: (...y: string[]) => void = function (.../*3*/y) {\n    var t = y;\n};\nfunction foo(x: (...y: string[]) => void ) { }\nfoo((.../*4*/y1) => {\n    var t = y;\n});\nfoo((/*5*/y2) => {\n    var t = y;\n});\nvar t1 :(a1: string, a2: string) => void = (.../*t1*/f1) => { }  // f1 => any[];\nvar t2: (a1: string, ...a2: string[]) => void = (.../*t2*/f1) => { } // f1 => any[];\nvar t3: (a1: number, a2: boolean, ...c: string[]) => void  = (/*t31*/f1, .../*t32*/f2) => { }; // f1 => number, f2 => any[]\nvar t4: (...a1: string[]) => void = (.../*t4*/f1) => { };      // f1 => string[]\nvar t5: (...a1: string[]) => void = (/*t5*/f1) => { };         // f1 => string\nvar t6: (...a1: string[]) => void = (/*t61*/f1, .../*t62*/f2) => { };  // f1 => string, f2 => string[]\nvar t7: (...a1: string[]) => void = (/*t71*/f1, /*t72*/f2, /*t73*/f3) => { }; // fa => string, f2 => string, f3 => string\n// Explicit type annotation\nvar t8: (...a1: string[]) => void = (/*t8*/f1: number[]) => { };\n// Explicit initialization value\nvar t9: (a1: string[], a2: string[]) => void = (/*t91*/f1 = 4, /*t92*/f2 = [false, true]) => { };"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(parameter) restArgs: any[]", "");
                f.VerifyQuickInfoAt(t, "2", "(parameter) restArgs: any[]", "");
                f.VerifyQuickInfoAt(t, "3", "(parameter) y: string[]", "");
                f.VerifyQuickInfoAt(t, "4", "(parameter) y1: string[]", "");
                f.VerifyQuickInfoAt(t, "5", "(parameter) y2: string", "");
                f.VerifyQuickInfoAt(t, "t1", "(parameter) f1: [a1: string, a2: string]", "");
                f.VerifyQuickInfoAt(t, "t2", "(parameter) f1: [a1: string, ...a2: string[]]", "");
                f.VerifyQuickInfoAt(t, "t31", "(parameter) f1: number", "");
                f.VerifyQuickInfoAt(t, "t32", "(parameter) f2: [a2: boolean, ...c: string[]]", "");
                f.VerifyQuickInfoAt(t, "t4", "(parameter) f1: string[]", "");
                f.VerifyQuickInfoAt(t, "t5", "(parameter) f1: string", "");
                f.VerifyQuickInfoAt(t, "t61", "(parameter) f1: string", "");
                f.VerifyQuickInfoAt(t, "t62", "(parameter) f2: string[]", "");
                f.VerifyQuickInfoAt(t, "t71", "(parameter) f1: string", "");
                f.VerifyQuickInfoAt(t, "t72", "(parameter) f2: string", "");
                f.VerifyQuickInfoAt(t, "t73", "(parameter) f3: string", "");
                f.VerifyQuickInfoAt(t, "t8", "(parameter) f1: number[]", "");
                f.VerifyQuickInfoAt(t, "t91", "(parameter) f1: string[]", "");
                f.VerifyQuickInfoAt(t, "t92", "(parameter) f2: string[]", "");
            }
            finally
            {
                done();
            }
        });
    }

}
