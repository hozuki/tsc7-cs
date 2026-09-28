// ported from: internal/fourslash/tests/quickInfoMappedSpreadTypes_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoMappedSpreadTypesCase
{
    public static void TestQuickInfoMappedSpreadTypes(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Foo {\n    /** Doc */\n    bar: number;\n}\n\nconst f: Foo = { bar: 0 };\nf./*f*/bar;\n\nconst f2: { [TKey in keyof Foo]: string } = { bar: \"0\" };\nf2./*f2*/bar;\n\nconst f3 = { ...f };\nf3./*f3*/bar;\n\nconst f4 = { ...f2 };\nf4./*f4*/bar;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "f");
                f.VerifyQuickInfoIs(t, "(property) Foo.bar: number", "Doc");
                f.GoToMarker(t, "f2");
                f.VerifyQuickInfoIs(t, "(property) bar: string", "Doc");
                f.GoToMarker(t, "f3");
                f.VerifyQuickInfoIs(t, "(property) Foo.bar: number", "Doc");
                f.GoToMarker(t, "f4");
                f.VerifyQuickInfoIs(t, "(property) bar: string", "Doc");
            }
            finally
            {
                done();
            }
        });
    }

}
