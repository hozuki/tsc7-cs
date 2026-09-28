// ported from: internal/fourslash/tests/declarationMapsGoToDefinitionSameNameDifferentDirectory_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class DeclarationMapsGoToDefinitionSameNameDifferentDirectoryCase
{
    public static void TestDeclarationMapsGoToDefinitionSameNameDifferentDirectory(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: BaseClass/Source.d.ts\ndeclare class Control {\n    constructor();\n    /** this is a super var */\n    myVar: boolean | 'yeah';\n}\n//# sourceMappingURL=Source.d.ts.map\n// @Filename: BaseClass/Source.d.ts.map\n{\"version\":3,\"file\":\"Source.d.ts\",\"sourceRoot\":\"\",\"sources\":[\"Source.ts\"],\"names\":[],\"mappings\":\"AAAA,cAAM,OAAO;;IAIT,0BAA0B;IACnB,KAAK,EAAE,OAAO,GAAG,MAAM,CAAQ;CACzC\"}\n// @Filename: BaseClass/Source.ts\nclass /*2*/Control{\n    constructor(){\n        return;\n    }\n    /** this is a super var */\n    public /*4*/myVar: boolean | 'yeah' = true;\n}\n// @Filename: tsbase.json\n{\n    \"$schema\": \"http://json.schemastore.org/tsconfig\",\n    \"compileOnSave\": true,\n    \"compilerOptions\": {\n      \"lib\": [\"es5\"],\n      \"strict\": false,\n      \"sourceMap\": true,\n      \"declaration\": true,\n      \"declarationMap\": true\n    }\n  }\n// @Filename: buttonClass/tsconfig.json\n{\n    \"extends\": \"../tsbase.json\",\n    \"compilerOptions\": {\n      \"outDir\": \".\"\n    },\n    \"files\": [\n      \"Source.ts\"\n    ],\n    \"include\": [\n      \"../BaseClass/Source.d.ts\"\n    ]\n  }\n// @Filename: buttonClass/Source.ts\n// I cannot F12 navigate to Control\n//                   vvvvvvv\nclass Button extends [|/*1*/Control|] {\n    public myFunction() {\n        // I cannot F12 navigate to myVar\n        //              vvvvv\n        if (typeof this.[|/*3*/myVar|] === 'boolean') {\n            this.myVar;\n        } else {\n            this.myVar.toLocaleUpperCase();\n        }\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.VerifyBaselineGoToDefinition(t, true, "1", "3");
            }
            finally
            {
                done();
            }
        });
    }

}
