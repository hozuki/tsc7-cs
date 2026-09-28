// ported from: internal/fourslash/tests/quickInforForSucessiveInferencesIsNotAny_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInforForSucessiveInferencesIsNotAnyCase
{
    public static void TestQuickInforForSucessiveInferencesIsNotAny(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("declare function schema<T> (value : T) : {field : T};\n\ndeclare const b: boolean;\nconst obj/*1*/ = schema(b);\nconst actualTypeOfNested/*2*/ = schema(obj);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "const obj: {\n    field: boolean;\n}", "");
                f.VerifyQuickInfoAt(t, "2", "const actualTypeOfNested: {\n    field: {\n        field: boolean;\n    };\n}", "");
            }
            finally
            {
                done();
            }
        });
    }

}
