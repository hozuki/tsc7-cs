// ported from: internal/fourslash/tests/automaticConstructorToggling_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutomaticConstructorTogglingCase
{
    public static void TestAutomaticConstructorToggling(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class A<T> { }\nclass B<T> {/*B*/ }\nclass C<T> { /*C*/constructor(val: T) { } }\nclass D<T> { constructor(/*D*/val: T) { } }\n\nnew /*Asig*/A<string>();\nnew /*Bsig*/B(\"\");\nnew /*Csig*/C(\"\");\nnew /*Dsig*/D<string>();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "B");
                f.Insert(t, "constructor(val: T) { }");
                f.VerifyQuickInfoAt(t, "Asig", "constructor A<string>(): A<string>", "");
                f.VerifyQuickInfoAt(t, "Bsig", "constructor B<string>(val: string): B<string>", "");
                f.VerifyQuickInfoAt(t, "Csig", "constructor C<string>(val: string): C<string>", "");
                f.VerifyQuickInfoAt(t, "Dsig", "constructor D<string>(val: string): D<string>", "");
                f.GoToMarker(t, "C");
                f.DeleteAtCaret(t, 23);
                f.VerifyQuickInfoAt(t, "Asig", "constructor A<string>(): A<string>", "");
                f.VerifyQuickInfoAt(t, "Bsig", "constructor B<string>(val: string): B<string>", "");
                f.VerifyQuickInfoAt(t, "Csig", "constructor C<unknown>(): C<unknown>", "");
                f.VerifyQuickInfoAt(t, "Dsig", "constructor D<string>(val: string): D<string>", "");
                f.GoToMarker(t, "D");
                f.DeleteAtCaret(t, 6);
                f.VerifyQuickInfoAt(t, "Asig", "constructor A<string>(): A<string>", "");
                f.VerifyQuickInfoAt(t, "Bsig", "constructor B<string>(val: string): B<string>", "");
                f.VerifyQuickInfoAt(t, "Csig", "constructor C<unknown>(): C<unknown>", "");
                f.VerifyQuickInfoAt(t, "Dsig", "constructor D<string>(): D<string>", "");
            }
            finally
            {
                done();
            }
        });
    }

}
