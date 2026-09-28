// ported from: internal/fourslash/tests/quickInfoForTypeParameterInTypeAlias1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoForTypeParameterInTypeAlias1Case
{
    public static void TestQuickInfoForTypeParameterInTypeAlias1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("type Ctor<AA> = new () => A/*1*/A;\ntype MixinCtor<AA> = new () => AA & { constructor: MixinCtor<A/*2*/A> };\ntype NestedCtor<AA> = new() => AA & (new () => AA & { constructor: NestedCtor<A/*3*/A> });\ntype Method<AA> = { method(): A/*4*/A };\ntype Construct<AA> = { new(): A/*5*/A };"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(type parameter) AA in type Ctor<AA>", "");
                f.VerifyQuickInfoAt(t, "2", "(type parameter) AA in type MixinCtor<AA>", "");
                f.VerifyQuickInfoAt(t, "3", "(type parameter) AA in type NestedCtor<AA>", "");
                f.VerifyQuickInfoAt(t, "4", "(type parameter) AA in type Method<AA>", "");
                f.VerifyQuickInfoAt(t, "5", "(type parameter) AA in type Construct<AA>", "");
            }
            finally
            {
                done();
            }
        });
    }

}
