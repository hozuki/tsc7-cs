// ported from: internal/fourslash/tests/quickInfoOnMergedInterfacesWithIncrementalEdits_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoOnMergedInterfacesWithIncrementalEditsCase
{
    public static void TestQuickInfoOnMergedInterfacesWithIncrementalEdits(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: false\nnamespace MM {\n    interface B<T> {\n        foo: number;\n    }\n    interface B<T> {\n        bar: string;\n    }\n    var b: B<string>;\n    var r3 = b.foo; // number\n    var r/*2*/4 = b.b/*1*/ar; // string\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.VerifyQuickInfoIs(t, "(property) B<string>.bar: string", "");
                f.DeleteAtCaret(t, 1);
                f.Insert(t, "z");
                f.VerifyQuickInfoIs(t, "any", "");
                f.VerifyNumberOfErrorsInCurrentFile(t, 1);
                f.Backspace(t, 1);
                f.Insert(t, "a");
                f.VerifyQuickInfoIs(t, "(property) B<string>.bar: string", "");
                f.GoToMarker(t, "2");
                f.VerifyQuickInfoIs(t, "var r4: string", "");
                f.VerifyNoErrors(t);
            }
            finally
            {
                done();
            }
        });
    }

}
