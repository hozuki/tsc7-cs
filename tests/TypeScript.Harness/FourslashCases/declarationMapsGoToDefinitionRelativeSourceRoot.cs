// ported from: internal/fourslash/tests/declarationMapsGoToDefinitionRelativeSourceRoot_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class DeclarationMapsGoToDefinitionRelativeSourceRootCase
{
    public static void TestDeclarationMapsGoToDefinitionRelativeSourceRoot(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n// @Filename: index.ts\nexport class Foo {\n    member: string;\n    /*2*/methodName(propName: SomeType): void {}\n    otherMethod() {\n        if (Math.random() > 0.5) {\n            return {x: 42};\n        }\n        return {y: \"yes\"};\n    }\n}\n\nexport interface SomeType {\n    member: number;\n}\n// @Filename: out/indexdef.d.ts.map\n{\"version\":3,\"file\":\"indexdef.d.ts\",\"sourceRoot\":\"../\",\"sources\":[\"index.ts\"],\"names\":[],\"mappings\":\"AAAA;IACI,MAAM,EAAE,MAAM,CAAC;IACf,UAAU,CAAC,QAAQ,EAAE,QAAQ,GAAG,IAAI;IACpC,WAAW;;;;;;;CAMd;AAED,MAAM,WAAW,QAAQ;IACrB,MAAM,EAAE,MAAM,CAAC;CAClB\"}\n// @Filename: out/indexdef.d.ts\nexport declare class Foo {\n    member: string;\n    methodName(propName: SomeType): void;\n    otherMethod(): {\n        x: number;\n        y?: undefined;\n    } | {\n        y: string;\n        x?: undefined;\n    };\n}\nexport interface SomeType {\n    member: number;\n}\n//# sourceMappingURL=out/indexdef.d.ts.map\n// @Filename: mymodule.ts\nimport * as mod from \"./out/indexdef\";\nconst instance = new mod.Foo();\ninstance.[|/*1*/methodName|]({member: 12});"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.VerifyBaselineGoToDefinition(t, true, "1");
            }
            finally
            {
                done();
            }
        });
    }

}
