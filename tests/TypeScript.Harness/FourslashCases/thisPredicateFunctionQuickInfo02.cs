// ported from: internal/fourslash/tests/thisPredicateFunctionQuickInfo02_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ThisPredicateFunctionQuickInfo02Case
{
    public static void TestThisPredicateFunctionQuickInfo02(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Sundries {\n    broken: boolean;\n}\n\ninterface Supplies {\n    spoiled: boolean;\n}\n\ninterface Crate<T> {\n    contents: T;\n    /*1*/isSundries(): this is Crate<Sundries>;\n    /*2*/isSupplies(): this is Crate<Supplies>;\n    /*3*/isPackedTight(): this is (this & {extraContents: T});\n}\nconst crate: Crate<any>;\nif (crate.isPackedTight/*4*/()) {\n    crate.;\n}\nif (crate.isSundries/*5*/()) {\n    crate.contents.;\n    if (crate.isPackedTight/*6*/()) {\n       crate.;\n    }\n}\nif (crate.isSupplies/*7*/()) {\n    crate.contents.;\n    if (crate.isPackedTight/*8*/()) {\n       crate.;\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(method) Crate<T>.isSundries(): this is Crate<Sundries>", "");
                f.VerifyQuickInfoAt(t, "2", "(method) Crate<T>.isSupplies(): this is Crate<Supplies>", "");
                f.VerifyQuickInfoAt(t, "3", "(method) Crate<T>.isPackedTight(): this is (this & {\n    extraContents: T;\n})", "");
                f.VerifyQuickInfoAt(t, "4", "(method) Crate<any>.isPackedTight(): this is (Crate<any> & {\n    extraContents: any;\n})", "");
                f.VerifyQuickInfoAt(t, "5", "(method) Crate<any>.isSundries(): this is Crate<Sundries>", "");
                f.VerifyQuickInfoAt(t, "6", "(method) Crate<Sundries>.isPackedTight(): this is (Crate<Sundries> & {\n    extraContents: Sundries;\n})", "");
                f.VerifyQuickInfoAt(t, "7", "(method) Crate<any>.isSupplies(): this is Crate<Supplies>", "");
                f.VerifyQuickInfoAt(t, "8", "(method) Crate<Supplies>.isPackedTight(): this is (Crate<Supplies> & {\n    extraContents: Supplies;\n})", "");
            }
            finally
            {
                done();
            }
        });
    }

}
