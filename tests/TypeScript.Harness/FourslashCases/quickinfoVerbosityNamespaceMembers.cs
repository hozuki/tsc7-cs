// ported from: internal/fourslash/tests/quickinfoVerbosityNamespaceMembers_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickinfoVerbosityNamespaceMembersCase
{
    public static void TestQuickinfoVerbosityNamespaceMembers(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\ndeclare namespace NS/*1*/ {\n    type StringAlias = string;\n    type Pair<T> = { first: T; second: T };\n\n    enum Color { Red, Green, Blue }\n\n    class MyClass {\n        name: string;\n        greet(): void;\n    }\n\n    interface MyInterface {\n        id: number;\n        label: string;\n    }\n\n    const value: number;\n    function doSomething(x: string): boolean;\n}\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHoverWithVerbosity(t, new Dictionary<string, int[]> { ["1"] = [0, 1] });
            }
            finally
            {
                done();
            }
        });
    }

}
