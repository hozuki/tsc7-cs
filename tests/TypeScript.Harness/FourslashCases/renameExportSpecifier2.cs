// ported from: internal/fourslash/tests/renameExportSpecifier2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameExportSpecifier2Case
{
    public static void TestRenameExportSpecifier2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: a.ts\nconst name = {};\nexport { name/**/ };\n// @Filename: b.ts\nimport { name } from './a';\nconst x = name.toString();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineRename(t, new LSUP.UserPreferences { UseAliasesForRename = TRI.TSFalse }, "");
            }
            finally
            {
                done();
            }
        });
    }

}
