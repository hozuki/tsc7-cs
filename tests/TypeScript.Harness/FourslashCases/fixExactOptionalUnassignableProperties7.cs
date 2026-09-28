// ported from: internal/fourslash/tests/fixExactOptionalUnassignableProperties7_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FixExactOptionalUnassignableProperties7Case
{
    public static void TestFixExactOptionalUnassignableProperties7(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strictNullChecks: true\n// @exactOptionalPropertyTypes: true\n// @Filename: fixExactOptionalUnassignableProperties6.ts\nclass Feh {\n    _requestFinished(error?: string) {\n        this._finishedPromiseCallback({ error/**/ });\n    }\n    private _finishedPromiseCallback: (arg: { error?: string }) => void = () => {};\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFixNotAvailable(t);
            }
            finally
            {
                done();
            }
        });
    }

}
