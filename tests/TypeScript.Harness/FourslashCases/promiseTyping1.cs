// ported from: internal/fourslash/tests/promiseTyping1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class PromiseTyping1Case
{
    public static void TestPromiseTyping1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface IPromise<T> {\n    then<U>(success: (value: T) => IPromise<U>, error?: (error: any) => IPromise<U>, progress?: (progress: any) => void ): IPromise<U>;\n    then<U>(success: (value: T) => IPromise<U>, error?: (error: any) => U, progress?: (progress: any) => void ): IPromise<U>;\n    then<U>(success: (value: T) => U, error?: (error: any) => IPromise<U>, progress?: (progress: any) => void ): IPromise<U>;\n    then<U>(success: (value: T) => U, error?: (error: any) => U, progress?: (progress: any) => void ): IPromise<U>;\n    done? <U>(success: (value: T) => any, error?: (error: any) => any, progress?: (progress: any) => void ): void;\n}\nvar p1: IPromise<string>;\nvar p/*1*/2 = p1.then(function (x/*2*/x) {\n    return xx;\n});\np2.then(function (x/*3*/x) {\n} );"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "var p2: IPromise<string>", "");
                f.VerifyQuickInfoAt(t, "2", "(parameter) xx: string", "");
                f.VerifyQuickInfoAt(t, "3", "(parameter) xx: string", "");
            }
            finally
            {
                done();
            }
        });
    }

}
