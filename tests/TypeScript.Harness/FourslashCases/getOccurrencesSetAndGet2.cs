// ported from: internal/fourslash/tests/getOccurrencesSetAndGet2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetOccurrencesSetAndGet2Case
{
    public static void TestGetOccurrencesSetAndGet2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class Foo {\n    set bar(b: any) {\n    }\n\n    public get bar(): any {\n        return undefined;\n    }\n\n    public [|set|] set(s: any) {\n    }\n\n    public [|get|] set(): any {\n        return undefined;\n    }\n\n    public set get(g: any) {\n    }\n\n    public get get(): any {\n        return undefined;\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineDocumentHighlights(t, null!, U.ToAny(f.Ranges()));
            }
            finally
            {
                done();
            }
        });
    }

}
