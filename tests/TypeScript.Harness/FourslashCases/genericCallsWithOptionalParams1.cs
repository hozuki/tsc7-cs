// ported from: internal/fourslash/tests/genericCallsWithOptionalParams1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GenericCallsWithOptionalParams1Case
{
    public static void TestGenericCallsWithOptionalParams1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class Collection<T> {\n    public add(x: T) { }\n}\ninterface Utils {\n    fold<T, S>(c: Collection<T>, folder: (s: S, t: T) => T, init?: S): T;\n}\nvar c = new Collection<string>();\nvar utils: Utils;\nvar /*1*/r = utils.fold(c, (s, t) => t, \"\");\nvar /*2*/r2 = utils.fold(c, (s, t) => t);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "var r: string", "");
                f.VerifyQuickInfoAt(t, "2", "var r2: string", "");
            }
            finally
            {
                done();
            }
        });
    }

}
