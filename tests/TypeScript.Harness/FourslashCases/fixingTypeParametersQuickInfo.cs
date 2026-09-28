// ported from: internal/fourslash/tests/fixingTypeParametersQuickInfo_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FixingTypeParametersQuickInfoCase
{
    public static void TestFixingTypeParametersQuickInfo(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: false\ndeclare function f<T>(x: T, y: (p: T) => T, z: (p: T) => T): T;\nvar /*1*/result = /*2*/f(0, /*3*/x => null, /*4*/x => x.blahblah);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "var result: number", "");
                f.VerifyQuickInfoAt(t, "2", "function f<number>(x: number, y: (p: number) => number, z: (p: number) => number): number", "");
                f.VerifyQuickInfoAt(t, "3", "(parameter) x: number", "");
                f.VerifyQuickInfoAt(t, "4", "(parameter) x: number", "");
            }
            finally
            {
                done();
            }
        });
    }

}
