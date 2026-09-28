// ported from: internal/fourslash/tests/promiseTyping2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class PromiseTyping2Case
{
    public static void TestPromiseTyping2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface IPromise<T> {\n    then<U>(success?: (value: T) => IPromise<U>, error?: (error: any) => IPromise<U>, progress?: (progress: any) => void ): IPromise<U>;\n    then<U>(success?: (value: T) => IPromise<U>, error?: (error: any) => U, progress?: (progress: any) => void ): IPromise<U>;\n    then<U>(success?: (value: T) => U, error?: (error: any) => IPromise<U>, progress?: (progress: any) => void ): IPromise<U>;\n    then<U>(success?: (value: T) => U, error?: (error: any) => U, progress?: (progress: any) => void ): IPromise<U>;\n    done? <U>(success?: (value: T) => any, error?: (error: any) => any, progress?: (progress: any) => void ): void;\n}\nvar p1: IPromise<number> = null;\np/*1*/1.then(function (x/*2*/x) { }); \nvar p/*3*/2 = p1.then(function (x/*4*/x) { return \"hello\"; })\nvar p/*5*/3 = p2.then(function (x/*6*/x) {\n    return x/*7*/x;\n});"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "var p1: IPromise<number>", "");
                f.VerifyQuickInfoAt(t, "2", "(parameter) xx: number", "");
                f.VerifyQuickInfoAt(t, "3", "var p2: IPromise<string>", "");
                f.VerifyQuickInfoAt(t, "4", "(parameter) xx: number", "");
                f.VerifyQuickInfoAt(t, "5", "var p3: IPromise<string>", "");
                f.VerifyQuickInfoAt(t, "6", "(parameter) xx: string", "");
                f.VerifyQuickInfoAt(t, "7", "(parameter) xx: string", "");
            }
            finally
            {
                done();
            }
        });
    }

}
