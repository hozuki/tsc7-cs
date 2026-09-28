// ported from: internal/fourslash/tests/inlayHintsInteractiveTemplateLiteralTypes_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class InlayHintsInteractiveTemplateLiteralTypesCase
{
    public static void TestInlayHintsInteractiveTemplateLiteralTypes(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("declare function getTemplateLiteral1(): `${string},${string}`;\nconst lit1 = getTemplateLiteral1();\ndeclare function getTemplateLiteral2(): `\\${${string},${string}`;\nconst lit2 = getTemplateLiteral2();\ndeclare function getTemplateLiteral3(): `start${string}\\${,$${string}end`;\nconst lit3 = getTemplateLiteral3();\ndeclare function getTemplateLiteral4(): `${string}\\`,${string}`;\nconst lit4 = getTemplateLiteral4();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineInlayHints(t, null!, new LSUP.UserPreferences { InlayHints = new LSUP.InlayHintsPreferences { IncludeInlayVariableTypeHints = TRI.TSTrue } });
            }
            finally
            {
                done();
            }
        });
    }

}
