// ported from: internal/fourslash/tests/symbolNameAtUnparseableFunctionOverload_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SymbolNameAtUnparseableFunctionOverloadCase
{
    public static void TestSymbolNameAtUnparseableFunctionOverload(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class TestClass {\n    public function foo(x: string): void;\n    public function foo(): void;\n    foo(x: any): void {\n        this.bar(/**/x); // should not error\n    }\n}\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifyQuickInfoExists(t);
            }
            finally
            {
                done();
            }
        });
    }

}
