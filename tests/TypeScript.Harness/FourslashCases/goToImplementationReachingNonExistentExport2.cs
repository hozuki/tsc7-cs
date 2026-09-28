// ported from: internal/fourslash/tests/goToImplementationReachingNonExistentExport2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToImplementationReachingNonExistentExport2Case
{
    public static void TestGoToImplementationReachingNonExistentExport2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\n// @allowJs: true\n// @checkJs: true\n\n// @Filename: /github.js\nmodule.exports = { transformRecordedData };\n\n// @Filename: /gitGateway.js\nconst { transformRecordedData: transformGitHub } = require('./github');\n\nconst methods = { github: {\n    transformData: /*impl*/transformGitHub,\n}};\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToImplementation(t, "impl");
            }
            finally
            {
                done();
            }
        });
    }

}
