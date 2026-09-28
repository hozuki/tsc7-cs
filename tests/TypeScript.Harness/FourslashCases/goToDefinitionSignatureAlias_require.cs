// ported from: internal/fourslash/tests/goToDefinitionSignatureAlias_require_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionSignatureAlias_requireCase
{
    public static void TestGoToDefinitionSignatureAlias_require(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: /a.js\nmodule.exports = function /*f*/f() {}\n// @Filename: /b.js\nconst f = require(\"./a\");\n[|/*use*/f|]();\n// @Filename: /bar.ts\nimport f = require(\"./a\");\n[|/*useTs*/f|]();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "use", "useTs");
            }
            finally
            {
                done();
            }
        });
    }

}
