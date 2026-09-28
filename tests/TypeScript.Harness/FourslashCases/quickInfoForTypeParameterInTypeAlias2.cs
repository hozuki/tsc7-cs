// ported from: internal/fourslash/tests/quickInfoForTypeParameterInTypeAlias2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoForTypeParameterInTypeAlias2Case
{
    public static void TestQuickInfoForTypeParameterInTypeAlias2(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("type Call<AA> = { (): A/*1*/A };\ntype Index<AA> = {[foo: string]: A/*2*/A};\ntype GenericMethod<AA> = { method<BB>(): A/*3*/A & B/*4*/B }\ntype Nesting<TT> = { method<UU>(): new <WW>() => T/*5*/T & U/*6*/U & W/*7*/W };"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(type parameter) AA in type Call<AA>", "");
                f.VerifyQuickInfoAt(t, "2", "(type parameter) AA in type Index<AA>", "");
                f.VerifyQuickInfoAt(t, "3", "(type parameter) AA in type GenericMethod<AA>", "");
                f.VerifyQuickInfoAt(t, "4", "(type parameter) BB in method<BB>(): AA & BB", "");
                f.VerifyQuickInfoAt(t, "5", "(type parameter) TT in type Nesting<TT>", "");
                f.VerifyQuickInfoAt(t, "6", "(type parameter) UU in method<UU>(): new <WW>() => TT & UU & WW", "");
                f.VerifyQuickInfoAt(t, "7", "(type parameter) WW in <WW>(): TT & UU & WW", "");
            }
            finally
            {
                done();
            }
        });
    }

}
