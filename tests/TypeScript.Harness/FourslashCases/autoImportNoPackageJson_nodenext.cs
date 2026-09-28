// ported from: internal/fourslash/tests/autoImportNoPackageJson_nodenext_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportNoPackageJson_nodenextCase
{
    public static void TestAutoImportNoPackageJson_nodenext(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n// @module: node18\n// @Filename: /node_modules/lit/index.d.cts\nexport declare function customElement(name: string): any;\n// @Filename: /a.ts\ncustomElement/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.Configure(t, new LSUP.UserPreferences { AutoImportEntrypointDirectorySearch = TRI.TSTrue });
                f.VerifyImportFixModuleSpecifiers(t, "", ["lit/index.cjs"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
