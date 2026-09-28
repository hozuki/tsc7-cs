// ported from: internal/fourslash/tests/tsxQuickInfo6_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class TsxQuickInfo6Case
{
    public static void TestTsxQuickInfo6(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("//@Filename: file.tsx\n// @jsx: preserve\n// @noLib: true\ndeclare function ComponentSpecific<U>(l: {prop: U}): JSX.Element;\ndeclare function ComponentSpecific1<U>(l: {prop: U, \"ignore-prop\": number}): JSX.Element;\nfunction Bar<T extends {prop: number}>(arg: T) {\n    let a1 = <Compone/*1*/ntSpecific {...arg} ignore-prop=\"hi\" />;  // U is number\n    let a2 = <ComponentSpecific1 {...arg} ignore-prop={10} />;  // U is number\n    let a3 = <Component/*2*/Specific {...arg} prop=\"hello\" />;   // U is \"hello\"\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "function ComponentSpecific<number>(l: {\n    prop: number;\n}): JSX.Element", "");
                f.VerifyQuickInfoAt(t, "2", "function ComponentSpecific<never>(l: {\n    prop: never;\n}): JSX.Element", "");
            }
            finally
            {
                done();
            }
        });
    }

}
