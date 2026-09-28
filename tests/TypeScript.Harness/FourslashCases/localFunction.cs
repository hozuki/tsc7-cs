// ported from: internal/fourslash/tests/localFunction_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class LocalFunctionCase
{
    public static void TestLocalFunction(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function /*1*/foo() {\n    function /*2*/bar2() {\n    }\n    var y = function /*3*/bar3() {\n    }\n}\nvar x = function /*4*/bar4() {\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "function foo(): void", "");
                f.VerifyQuickInfoAt(t, "2", "(local function) bar2(): void", "");
                f.VerifyQuickInfoAt(t, "3", "(local function) bar3(): void", "");
                f.VerifyQuickInfoAt(t, "4", "(local function) bar4(): void", "");
            }
            finally
            {
                done();
            }
        });
    }

}
