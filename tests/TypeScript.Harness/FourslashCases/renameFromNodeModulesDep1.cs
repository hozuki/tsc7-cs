// ported from: internal/fourslash/tests/renameFromNodeModulesDep1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameFromNodeModulesDep1Case
{
    public static void TestRenameFromNodeModulesDep1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /index.ts\nimport { /*okWithAlias*/[|Foo|] } from \"foo\";\ndeclare const f: Foo;\nf./*notOk*/bar;\n// @Filename: /tsconfig.json\n { }\n// @Filename: /node_modules/foo/package.json\n { \"types\": \"index.d.ts\" }\n// @Filename: /node_modules/foo/index.d.ts\nexport interface Foo {\n    bar: string;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "okWithAlias");
                f.VerifyRenameSucceeded(t, new LSUP.UserPreferences { UseAliasesForRename = TRI.TSTrue });
                f.VerifyRenameFailed(t, new LSUP.UserPreferences { UseAliasesForRename = TRI.TSFalse });
                f.GoToMarker(t, "notOk");
                f.VerifyRenameFailed(t, null!);
            }
            finally
            {
                done();
            }
        });
    }

}
