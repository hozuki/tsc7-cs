// ported from: internal/fourslash/tests/indexerReturnTypes1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class IndexerReturnTypes1Case
{
    public static void TestIndexerReturnTypes1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Numeric {\n    [x: number]: Date;\n}\n}\ninterface Stringy {\n    [x: string]: RegExp;\n}\n}\ninterface NumericPlus {\n    [x: number]: Date;\n    foo(): Date;\n}\n}\ninterface StringyPlus {\n    [x: string]: RegExp;\n    foo(): RegExp;\n}\n}\ninterface NumericG<T> {\n    [x: number]: T;\n}\n}\ninterface StringyG<T> {\n    [x: string]: T;\n}\n}\ninterface Ty<T> {\n    [x: number]: Ty<T>;\n}\ninterface Ty2<T> {\n    [x: number]: { [x: number]: T };\n}\n\n\n}\nvar numeric: Numeric;\nvar stringy: Stringy;\nvar numericPlus: NumericPlus;\nvar stringPlus: StringyPlus;\nvar numericG: NumericG<Date>;\nvar stringyG: StringyG<Date>;\nvar ty: Ty<Date>;\nvar ty2: Ty2<Date>;\n\nvar /*1*/r1 = numeric[1];\nvar /*2*/r2 = numeric['1'];\nvar /*3*/r3 = stringy[1];\nvar /*4*/r4 = stringy['1'];\nvar /*5*/r5 = numericPlus[1];\nvar /*6*/r6 = numericPlus['1'];\nvar /*7*/r7 = stringPlus[1];\nvar /*8*/r8 = stringPlus['1'];\nvar /*9*/r9 = numericG[1];\nvar /*10*/r10 = numericG['1'];\nvar /*11*/r11 = stringyG[1];\nvar /*12*/r12 = stringyG['1'];\nvar /*13*/r13 = ty[1];\nvar /*14*/r14 = ty['1'];\nvar /*15*/r15 = ty2[1];\nvar /*16*/r16 = ty2['1'];"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "var r1: Date", "");
                f.VerifyQuickInfoAt(t, "2", "var r2: Date", "");
                f.VerifyQuickInfoAt(t, "3", "var r3: RegExp", "");
                f.VerifyQuickInfoAt(t, "4", "var r4: RegExp", "");
                f.VerifyQuickInfoAt(t, "5", "var r5: Date", "");
                f.VerifyQuickInfoAt(t, "6", "var r6: Date", "");
                f.VerifyQuickInfoAt(t, "7", "var r7: RegExp", "");
                f.VerifyQuickInfoAt(t, "8", "var r8: RegExp", "");
                f.VerifyQuickInfoAt(t, "9", "var r9: Date", "");
                f.VerifyQuickInfoAt(t, "10", "var r10: Date", "");
                f.VerifyQuickInfoAt(t, "11", "var r11: Date", "");
                f.VerifyQuickInfoAt(t, "12", "var r12: Date", "");
                f.VerifyQuickInfoAt(t, "13", "var r13: Ty<Date>", "");
                f.VerifyQuickInfoAt(t, "14", "var r14: Ty<Date>", "");
                f.VerifyQuickInfoAt(t, "15", "var r15: {\n    [x: number]: Date;\n}", "");
                f.VerifyQuickInfoAt(t, "16", "var r16: {\n    [x: number]: Date;\n}", "");
            }
            finally
            {
                done();
            }
        });
    }

}
