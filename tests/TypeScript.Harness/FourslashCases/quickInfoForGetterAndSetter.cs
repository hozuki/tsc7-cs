// ported from: internal/fourslash/tests/quickInfoForGetterAndSetter_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoForGetterAndSetterCase
{
    public static void TestQuickInfoForGetterAndSetter(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class Test {\n    constructor() {\n        this.value;\n    }\n\n    /** Getter text */\n    get val/*1*/ue() {\n        return this.value;\n    }\n\n    /** Setter text */\n    set val/*2*/ue(value) {\n        this.value = value;\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.VerifyQuickInfoIs(t, "(getter) Test.value: any", "Getter text");
                f.GoToMarker(t, "2");
                f.VerifyQuickInfoIs(t, "(setter) Test.value: any", "Setter text");
            }
            finally
            {
                done();
            }
        });
    }

}
