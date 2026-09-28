// ported from: internal/fourslash/tests/referencesIsAvailableThroughGlobalNoCrash_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ReferencesIsAvailableThroughGlobalNoCrashCase
{
    public static void TestReferencesIsAvailableThroughGlobalNoCrash(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /packages/playwright-core/bundles/utils/node_modules/@types/debug/index.d.ts\ndeclare var debug: debug.Debug & { debug: debug.Debug; default: debug.Debug };\nexport = debug;\nexport as namespace debug;\ndeclare namespace debug {\n    interface Debug {\n       coerce: (val: any) => any;\n    }\n}\n// @Filename: /packages/playwright-core/bundles/utils/node_modules/@types/debug/package.json\n{ \"types\": \"index.d.ts\" }\n// @Filename: /packages/playwright-core/src/index.ts\nexport const debug: typeof import('../bundles/utils/node_modules//*1*/@types/debug') = require('./utilsBundleImpl').debug;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1");
            }
            finally
            {
                done();
            }
        });
    }

}
