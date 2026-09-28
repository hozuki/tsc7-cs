// ported from: internal/fourslash/tests/quickInfoTypedGenericPrototypeMember_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoTypedGenericPrototypeMemberCase
{
    public static void TestQuickInfoTypedGenericPrototypeMember(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class C<T> {\n   foo(x: T) { }\n}\nvar /*1*/x = new C<any>(); // Quick Info for x is C<any>\nvar /*2*/y = C.prototype; // Quick Info for y is C<{}>"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "var x: C<any>", "");
                f.VerifyQuickInfoAt(t, "2", "var y: C<any>", "");
            }
            finally
            {
                done();
            }
        });
    }

}
