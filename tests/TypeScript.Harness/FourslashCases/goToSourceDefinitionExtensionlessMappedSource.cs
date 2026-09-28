// ported from: internal/fourslash/tests/goToSourceDefinitionExtensionlessMappedSource_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToSourceDefinitionExtensionlessMappedSourceCase
{
    public static void TestGoToSourceDefinitionExtensionlessMappedSource(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /lib/helper.d.ts\nexport declare function helper(): string;\n//# sourceMappingURL=helper.d.ts.map\n// @Filename: /lib/helper.d.ts.map\n{\"version\":3,\"file\":\"helper.d.ts\",\"sourceRoot\":\"\",\"sources\":[\"helper\"],\"names\":[],\"mappings\":\"AAAA,wBAAgB,MAAM,IAAI,MAAM,CAAC\"}\n// @Filename: /lib/helper\nexport function helper(): string { return \"\"; }\n// @Filename: /index.ts\nimport { /*usage*/helper } from \"./lib/helper\";\nhelper();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToSourceDefinition(t, "usage");
            }
            finally
            {
                done();
            }
        });
    }

}
