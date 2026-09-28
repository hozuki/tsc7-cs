// ported from: internal/fourslash/tests/getOccurrencesClassExpressionPublic_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetOccurrencesClassExpressionPublicCase
{
    public static void TestGetOccurrencesClassExpressionPublic(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("let A = class Foo {\n    [|public|] foo;\n    [|public|] public;\n    constructor([|public|] y: string, private x: string) {\n    }\n    [|public|] method() { }\n    private method2() {}\n    [|public|] static static() { }\n}\n\nlet B = class D {\n    constructor(private x: number) {\n    }\n    private test() {}\n    public test2() {}\n}"u8);
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
