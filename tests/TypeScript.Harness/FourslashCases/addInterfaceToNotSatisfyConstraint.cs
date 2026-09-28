// ported from: internal/fourslash/tests/addInterfaceToNotSatisfyConstraint_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AddInterfaceToNotSatisfyConstraintCase
{
    public static void TestAddInterfaceToNotSatisfyConstraint(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface A {\n\ta: number;\n}\n/**/\ninterface C<T extends A> {\n    x: T;\n}\n\nvar v2: C<B>; // should not work"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.Insert(t, "interface B { b: string; }");
            }
            finally
            {
                done();
            }
        });
    }

}
