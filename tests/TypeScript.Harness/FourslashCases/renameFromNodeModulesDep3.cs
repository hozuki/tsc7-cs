// ported from: internal/fourslash/tests/renameFromNodeModulesDep3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameFromNodeModulesDep3Case
{
    public static void TestRenameFromNodeModulesDep3(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /packages/first/index.d.ts\nimport { /*ok*/[|Foo|] } from \"foo\";\ndeclare type FooBar = Foo[/*ok2*/\"[|bar|]\"];\n// @Filename: /packages/foo/package.json\n { \"types\": \"index.d.ts\" }\n// @Filename: /packages/foo/index.d.ts\nexport interface Foo {\n    /*ok3*/[|bar|]: string;\n}\n// @link: /packages/foo -> /packages/first/node_modules/foo"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "ok");
                f.VerifyRenameSucceeded(t, new LSUP.UserPreferences { UseAliasesForRename = TRI.TSTrue });
                f.VerifyRenameSucceeded(t, new LSUP.UserPreferences { UseAliasesForRename = TRI.TSFalse });
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
