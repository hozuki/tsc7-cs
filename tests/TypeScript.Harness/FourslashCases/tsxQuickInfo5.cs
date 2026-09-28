// ported from: internal/fourslash/tests/tsxQuickInfo5_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class TsxQuickInfo5Case
{
    public static void TestTsxQuickInfo5(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("//@Filename: file.tsx\n// @jsx: preserve\n// @noLib: true\ndeclare function ComponentWithTwoAttributes<K,V>(l: {key1: K, value: V}): JSX.Element;\nfunction Baz<T,U>(key1: T, value: U) {\n    let a0 = <ComponentWi/*1*/thTwoAttributes k/*2*/ey1={key1} val/*3*/ue={value} />\n    let a1 = <ComponentWithTwoAttributes {...{key1, value: value}} key=\"Component\" />\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "function ComponentWithTwoAttributes<T, U>(l: {\n    key1: T;\n    value: U;\n}): JSX.Element", "");
                f.VerifyQuickInfoAt(t, "2", "(property) key1: T", "");
                f.VerifyQuickInfoAt(t, "3", "(property) value: U", "");
            }
            finally
            {
                done();
            }
        });
    }

}
