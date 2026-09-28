// ported from: internal/fourslash/tests/codefixCrashExportGlobal_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodefixCrashExportGlobalCase
{
    public static void TestCodefixCrashExportGlobal(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: commonjs\n// @esModuleInterop: false\n// @allowSyntheticDefaultImports: false\n// @Filename: bar.ts\nimport * as foo from './foo'\nexport as namespace foo\nexport = foo;\n\ndeclare global {\n    const foo: typeof foo;\n}\n// @Filename: foo.d.ts\ninterface Root {\n    /**\n     * A .default property for ES6 default import compatibility\n     */\n    default: Root;\n}\n\ndeclare const root: Root;\nexport = root;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "bar.ts");
                f.VerifyCodeFixNotAvailable(t);
                f.GoToFile(t, "foo.d.ts");
                f.VerifyCodeFixNotAvailable(t);
            }
            finally
            {
                done();
            }
        });
    }

}
