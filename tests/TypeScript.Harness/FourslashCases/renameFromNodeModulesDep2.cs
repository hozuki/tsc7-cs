// ported from: internal/fourslash/tests/renameFromNodeModulesDep2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameFromNodeModulesDep2Case
{
    public static void TestRenameFromNodeModulesDep2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /node_modules/first/index.d.ts\nimport { /*okWithAlias*/[|Foo|] } from \"foo\";\ndeclare type FooBar = Foo[/*notOk*/\"bar\"];\n// @Filename: /node_modules/first/node_modules/foo/package.json\n { \"types\": \"index.d.ts\" }\n// @Filename: /node_modules/first/node_modules/foo/index.d.ts\nexport interface Foo {\n    /*ok2*/[|bar|]: string;\n}\n// @Filename: /node_modules/first/node_modules/foo/bar.d.ts\nimport { Foo } from \"./index\";\ndeclare type FooBar = Foo[/*ok3*/\"[|bar|]\"];"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "okWithAlias");
                f.VerifyRenameSucceeded(t, new LSUP.UserPreferences { UseAliasesForRename = TRI.TSTrue });
                f.VerifyRenameFailed(t, new LSUP.UserPreferences { UseAliasesForRename = TRI.TSFalse });
                f.GoToMarker(t, "notOk");
                f.VerifyRenameFailed(t, null!);
                f.GoToMarker(t, "ok2");
                f.VerifyRenameSucceeded(t, null!);
                f.GoToMarker(t, "ok3");
                f.VerifyRenameSucceeded(t, null!);
            }
            finally
            {
                done();
            }
        });
    }

}
