// ported from: internal/fourslash/tests/quickInfoForContextuallyTypedFunctionInReturnStatement_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoForContextuallyTypedFunctionInReturnStatementCase
{
    public static void TestQuickInfoForContextuallyTypedFunctionInReturnStatement(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Accumulator {\n    clear(): void;\n    add(x: number): void;\n    result(): number;\n}\n\nfunction makeAccumulator(): Accumulator {\n    var sum = 0;\n    return {\n        clear: function () { sum = 0; },\n        add: function (val/**/ue) { sum += value; },\n        result: function () { return sum; }\n    };\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "", "(parameter) value: number", "");
            }
            finally
            {
                done();
            }
        });
    }

}
