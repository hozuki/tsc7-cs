// ported from: internal/fourslash/tests/genericWithSpecializedProperties1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GenericWithSpecializedProperties1Case
{
    public static void TestGenericWithSpecializedProperties1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Foo<T> {\n    x: Foo<string>;\n    y: Foo<number>;\n}\nvar f: Foo<number>;\nvar /*1*/xx = f.x;\nvar /*2*/yy = f.y;\nvar f2: Foo<string>;\nvar /*3*/x2 = f2.x;\nvar /*4*/y2 = f2.y;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "var xx: Foo<string>", "");
                f.VerifyQuickInfoAt(t, "2", "var yy: Foo<number>", "");
                f.VerifyQuickInfoAt(t, "3", "var x2: Foo<string>", "");
                f.VerifyQuickInfoAt(t, "4", "var y2: Foo<number>", "");
            }
            finally
            {
                done();
            }
        });
    }

}
