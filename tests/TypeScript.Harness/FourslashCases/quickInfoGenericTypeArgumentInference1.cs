// ported from: internal/fourslash/tests/quickInfoGenericTypeArgumentInference1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoGenericTypeArgumentInference1Case
{
    public static void TestQuickInfoGenericTypeArgumentInference1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: false\nnamespace Underscore {\n    export interface Iterator<T, U> {\n        (value: T, index: any, list: any): U;\n    }\n\n    export interface Static {\n        all<T>(list: T[], iterator?: Iterator<T, boolean>, context?: any): T;\n        identity<T>(value: T): T;\n    }\n}\n\ndeclare var _: Underscore.Static;\nvar /*1*/r = _./*11*/all([true, 1, null, 'yes'], x => !x);\nvar /*2*/r2 = _./*21*/all([true], _.identity);\nvar /*3*/r3 = _./*31*/all([], _.identity);\nvar /*4*/r4 = _./*41*/all([<any>true], _.identity);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "var r: string | number | boolean", "");
                f.VerifyQuickInfoAt(t, "11", "(method) Underscore.Static.all<string | number | boolean>(list: (string | number | boolean)[], iterator?: Underscore.Iterator<string | number | boolean, boolean>, context?: any): string | number | boolean", "");
                f.VerifyQuickInfoAt(t, "2", "var r2: boolean", "");
                f.VerifyQuickInfoAt(t, "21", "(method) Underscore.Static.all<boolean>(list: boolean[], iterator?: Underscore.Iterator<boolean, boolean>, context?: any): boolean", "");
                f.VerifyQuickInfoAt(t, "3", "var r3: any", "");
                f.VerifyQuickInfoAt(t, "31", "(method) Underscore.Static.all<any>(list: any[], iterator?: Underscore.Iterator<any, boolean>, context?: any): any", "");
                f.VerifyQuickInfoAt(t, "4", "var r4: any", "");
                f.VerifyQuickInfoAt(t, "41", "(method) Underscore.Static.all<any>(list: any[], iterator?: Underscore.Iterator<any, boolean>, context?: any): any", "");
                f.VerifyNoErrors(t);
            }
            finally
            {
                done();
            }
        });
    }

}
