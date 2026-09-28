// ported from: internal/fourslash/tests/quickInfoForGenericPrototypeMember_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoForGenericPrototypeMemberCase
{
    public static void TestQuickInfoForGenericPrototypeMember(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class C<T> {\n   foo(x: T) { }\n}\nvar x = new /*1*/C<any>();\nvar y = C.proto/*2*/type;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "constructor C<any>(): C<any>", "");
                f.VerifyQuickInfoAt(t, "2", "(property) C<T>.prototype: C<any>", "");
            }
            finally
            {
                done();
            }
        });
    }

}
