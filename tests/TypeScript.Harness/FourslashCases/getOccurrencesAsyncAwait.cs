// ported from: internal/fourslash/tests/getOccurrencesAsyncAwait_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetOccurrencesAsyncAwaitCase
{
    public static void TestGetOccurrencesAsyncAwait(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("[|async|] function f() {\n [|await|] 100;\n [|a/**/wait|] [|await|] 200;\nclass Foo {\n    async memberFunction() {\n        await 1;\n    }\n}\n return [|await|] async function () {\n   await 300;\n }\n}\nasync function g() {\n    await 300;\n    async function f() {\n        await 400;\n    }\n}"u8);
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
