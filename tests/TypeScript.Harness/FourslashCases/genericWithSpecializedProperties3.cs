// ported from: internal/fourslash/tests/genericWithSpecializedProperties3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GenericWithSpecializedProperties3Case
{
    public static void TestGenericWithSpecializedProperties3(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Foo<T, U> {\n    x: Foo<T, U>;\n    y: Foo<U, U>;\n}\nvar f: Foo<number, string>;\nvar /*1*/xx = f.x;\nvar /*2*/yy = f.y;\nvar f2: Foo<string, number>;\nvar /*3*/x2 = f2.x;\nvar /*4*/y2 = f2.y;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "var xx: Foo<number, string>", "");
                f.VerifyQuickInfoAt(t, "2", "var yy: Foo<string, string>", "");
                f.VerifyQuickInfoAt(t, "3", "var x2: Foo<string, number>", "");
                f.VerifyQuickInfoAt(t, "4", "var y2: Foo<number, number>", "");
            }
            finally
            {
                done();
            }
        });
    }

}
