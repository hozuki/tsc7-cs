// ported from: internal/fourslash/tests/goToImplementationNoCrashTripleSlashRef2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToImplementationNoCrashTripleSlashRef2Case
{
    public static void TestGoToImplementationNoCrashTripleSlashRef2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /node_modules/@types/react/index.d.ts\nexport type JSX = {};\n\n// @Filename: /node_modules/excalidraw/index.d.ts\n/// <reference types=\"react\" />\n\n// @Filename: /index.ts\nimport type {JSX} from '/*m*/react';\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToImplementation(t, "m");
            }
            finally
            {
                done();
            }
        });
    }

}
