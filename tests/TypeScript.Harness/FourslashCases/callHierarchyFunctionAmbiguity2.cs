// ported from: internal/fourslash/tests/callHierarchyFunctionAmbiguity2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CallHierarchyFunctionAmbiguity2Case
{
    public static void TestCallHierarchyFunctionAmbiguity2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @filename: a.d.ts\ndeclare function /**/foo(x?: number): void;\n// @filename: b.d.ts\ndeclare function foo(x?: string): void;\ndeclare function foo(x?: boolean): void;\n// @filename: main.ts\nfunction bar() {\n    foo();\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifyBaselineCallHierarchy(t);
            }
            finally
            {
                done();
            }
        });
    }

}
