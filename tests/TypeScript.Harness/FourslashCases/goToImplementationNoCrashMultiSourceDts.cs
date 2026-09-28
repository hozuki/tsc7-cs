// ported from: internal/fourslash/tests/goToImplementationNoCrashMultiSourceDts_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToImplementationNoCrashMultiSourceDtsCase
{
    public static void TestGoToImplementationNoCrashMultiSourceDts(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\n// @Filename: /a.ts\nexport {};\n// @Filename: /b.ts\nexport {};\n// @Filename: /combined.d.ts\nexport declare class Bar {\n    method(): void;\n}\n//# sourceMappingURL=combined.d.ts.map\n// @Filename: /combined.d.ts.map\n{\"version\":3,\"file\":\"combined.d.ts\",\"sourceRoot\":\"\",\"sources\":[\"a.ts\",\"b.ts\"],\"names\":[],\"mappings\":\";IAAA,OCAA;AAAA\"}\n// @Filename: /user.ts\nimport { Bar } from './combined';\ndeclare const bar: Bar;\nbar./*impl*/method();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToImplementation(t, "impl");
            }
            finally
            {
                done();
            }
        });
    }

}
