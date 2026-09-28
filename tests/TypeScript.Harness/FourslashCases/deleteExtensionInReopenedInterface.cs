// ported from: internal/fourslash/tests/deleteExtensionInReopenedInterface_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class DeleteExtensionInReopenedInterfaceCase
{
    public static void TestDeleteExtensionInReopenedInterface(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface A { a: number; }\ninterface B { b: number; }\n\ninterface I /*del*/extends A { }\ninterface I extends B { }\n\nvar i: I;\nclass C /*delImplements*/implements A { }\nvar c: C;\nc.a;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "del");
                f.DeleteAtCaret(t, 9);
                f.GoToEOF(t);
                f.Insert(t, "var a = i.a;");
                f.GoToMarker(t, "delImplements");
                f.DeleteAtCaret(t, 12);
                f.GoToMarker(t, "del");
                f.Insert(t, "extends A");
            }
            finally
            {
                done();
            }
        });
    }

}
