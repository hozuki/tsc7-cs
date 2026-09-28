// ported from: internal/fourslash/tests/contentMapperDeclarationMapNavigation_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ContentMapperDeclarationMapNavigationCase
{
    public static void TestContentMapperDeclarationMapNavigation(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\n// @Filename: /node_modules/component/package.json\n{\n    \"name\": \"component\",\n    \"version\": \"1.0.0\",\n    \"types\": \"component.d.vue.ts\"\n}\n\n// @Filename: /node_modules/component/component.vue\nexport interface ComponentProps { emoji: \"\U0001F600\"; label: string; }\nexport declare const /*source*/component: ComponentProps;\n\n// @Filename: /node_modules/component/component.d.vue.ts\nexport interface ComponentProps {\n    emoji: \"\U0001F600\";\n    label: string;\n}\nexport declare const component: ComponentProps;\n//# sourceMappingURL=component.d.vue.ts.map\n\n// @Filename: /node_modules/component/component.d.vue.ts.map\n{\"version\":3,\"file\":\"component.d.vue.ts\",\"sourceRoot\":\"\",\"sources\":[\"component.vue\"],\"names\":[],\"mappings\":\"AAAA,MAAM,WAAW,cAAc;IAAG,KAAK,EAAE,IAAI,CAAC;IAAC,KAAK,EAAE,MAAM,CAAC;CAAE;AAC/D,MAAM,CAAC,OAAO,CAAC,MAAM,SAAS,EAAE,cAAc,CAAC\"}\n\n// @Filename: /main.ts\nimport { component } from \"component\";\n/*use*/component.label;\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.VerifyBaselineGoToDefinition(t, true, "use");
                f.VerifyBaselineFindAllReferences(t, "use");
            }
            finally
            {
                done();
            }
        });
    }

}
