// ported from: internal/fourslash/tests/declarationMapsOutOfDateMapping_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class DeclarationMapsOutOfDateMappingCase
{
    public static void TestDeclarationMapsOutOfDateMapping(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n// @Filename: /home/src/workspaces/project/node_modules/a/dist/index.d.ts\nexport declare class Foo {\n    bar: any;\n}\n//# sourceMappingURL=index.d.ts.map\n// @Filename: /home/src/workspaces/project/node_modules/a/dist/index.d.ts.map\n{\"version\":3,\"file\":\"index.d.ts\",\"sourceRoot\":\"\",\"sources\":[\"../src/index.ts\"],\"names\":[],\"mappings\":\"AAAA,qBAAa,GAAG;IACZ,GAAG,MAAC;CACP\"}\n// @Filename: /home/src/workspaces/project/node_modules/a/src/index.ts\nexport class /*2*/Foo {\n}\n\n// @Filename: /home/src/workspaces/project/node_modules/a/package.json\n{\n    \"name\": \"a\",\n    \"version\": \"0.0.0\",\n    \"private\": true,\n    \"main\": \"dist\",\n    \"types\": \"dist\"\n}\n// @Filename: /home/src/workspaces/project/index.ts\nimport { Foo/*1*/ } from \"a\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.GoToFile(t, "/home/src/workspaces/project/index.ts");
                f.VerifyBaselineGoToDefinition(t, false, "1");
            }
            finally
            {
                done();
            }
        });
    }

}
