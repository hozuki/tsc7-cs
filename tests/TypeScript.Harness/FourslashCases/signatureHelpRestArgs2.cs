// ported from: internal/fourslash/tests/signatureHelpRestArgs2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpRestArgs2Case
{
    public static void TestSignatureHelpRestArgs2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: true\n// @allowJs: true\n// @checkJs: true\n// @filename: index.js\nconst promisify = function (thisArg, fnName) {\n    const fn = thisArg[fnName];\n    return function () {\n        return new Promise((resolve) => {\n            fn.call(thisArg, ...arguments, /*1*/);\n        });\n    };\n};"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineSignatureHelp(t);
            }
            finally
            {
                done();
            }
        });
    }

}
