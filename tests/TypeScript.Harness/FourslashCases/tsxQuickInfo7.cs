// ported from: internal/fourslash/tests/tsxQuickInfo7_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class TsxQuickInfo7Case
{
    public static void TestTsxQuickInfo7(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("//@Filename: file.tsx\n// @jsx: preserve\n// @noLib: true\ndeclare function OverloadComponent<U>(attr: {b: U, a?: string, \"ignore-prop\": boolean}): JSX.Element;\ndeclare function OverloadComponent<T, U>(attr: {b: U, a: T}): JSX.Element;\ndeclare function OverloadComponent(): JSX.Element; // effective argument type of `{}`, needs to be last\nfunction Baz<T extends {b: number}, U extends {a: boolean, b:string}>(arg1: T, arg2: U) {\n    let a0 = <Overloa/*1*/dComponent {...arg1} a=\"hello\" ignore-prop />;\n    let a1 = <Overloa/*2*/dComponent {...arg2} ignore-pro=\"hello world\" />;\n    let a2 = <Overloa/*3*/dComponent {...arg2} />;\n    let a3 = <Overloa/*4*/dComponent {...arg1} ignore-prop />;\n    let a4 = <Overloa/*5*/dComponent />;\n    let a5 = <Overloa/*6*/dComponent {...arg2} ignore-prop=\"hello\" {...arg1} />;\n    let a6 = <Overloa/*7*/dComponent {...arg1} ignore-prop {...arg2} />;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "function OverloadComponent<number>(attr: {\n    b: number;\n    a?: string;\n    \"ignore-prop\": boolean;\n}): JSX.Element (+2 overloads)", "");
                f.VerifyQuickInfoAt(t, "2", "function OverloadComponent<boolean, string>(attr: {\n    b: string;\n    a: boolean;\n}): JSX.Element (+2 overloads)", "");
                f.VerifyQuickInfoAt(t, "3", "function OverloadComponent<boolean, string>(attr: {\n    b: string;\n    a: boolean;\n}): JSX.Element (+2 overloads)", "");
                f.VerifyQuickInfoAt(t, "4", "function OverloadComponent(): JSX.Element (+2 overloads)", "");
                f.VerifyQuickInfoAt(t, "5", "function OverloadComponent(): JSX.Element (+2 overloads)", "");
                f.VerifyQuickInfoAt(t, "6", "function OverloadComponent<boolean, never>(attr: {\n    b: never;\n    a: boolean;\n}): JSX.Element (+2 overloads)", "");
                f.VerifyQuickInfoAt(t, "7", "function OverloadComponent<boolean, never>(attr: {\n    b: never;\n    a: boolean;\n}): JSX.Element (+2 overloads)", "");
            }
            finally
            {
                done();
            }
        });
    }

}
