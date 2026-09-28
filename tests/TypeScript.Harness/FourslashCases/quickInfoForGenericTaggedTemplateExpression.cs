// ported from: internal/fourslash/tests/quickInfoForGenericTaggedTemplateExpression_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoForGenericTaggedTemplateExpressionCase
{
    public static void TestQuickInfoForGenericTaggedTemplateExpression(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface T1 {}\nclass T2 {}\ntype T3 = \"a\" | \"b\";\n\ndeclare function foo<T>(strings: TemplateStringsArray, ...values: T[]): void;\n\n/*1*/foo<number>``;\n/*2*/foo<string | number>``;\n/*3*/foo<{ a: number }>``;\n/*4*/foo<T1>``;\n/*5*/foo<T2>``;\n/*6*/foo<T3>``;\n/*7*/foo``;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "function foo<number>(strings: TemplateStringsArray, ...values: number[]): void", "");
                f.VerifyQuickInfoAt(t, "2", "function foo<string | number>(strings: TemplateStringsArray, ...values: (string | number)[]): void", "");
                f.VerifyQuickInfoAt(t, "3", "function foo<{\n    a: number;\n}>(strings: TemplateStringsArray, ...values: {\n    a: number;\n}[]): void", "");
                f.VerifyQuickInfoAt(t, "4", "function foo<T1>(strings: TemplateStringsArray, ...values: T1[]): void", "");
                f.VerifyQuickInfoAt(t, "5", "function foo<T2>(strings: TemplateStringsArray, ...values: T2[]): void", "");
                f.VerifyQuickInfoAt(t, "6", "function foo<T3>(strings: TemplateStringsArray, ...values: T3[]): void", "");
                f.VerifyQuickInfoAt(t, "7", "function foo<unknown>(strings: TemplateStringsArray, ...values: unknown[]): void", "");
            }
            finally
            {
                done();
            }
        });
    }

}
