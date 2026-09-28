// ported from: internal/fourslash/tests/autoImportProvider_referencesCrash_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportProvider_referencesCrashCase
{
    public static void TestAutoImportProvider_referencesCrash(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = "// @Filename: /home/src/workspaces/project/a/package.json\n{}\n// @Filename: /home/src/workspaces/project/a/tsconfig.json\n{ \"compilerOptions\": { \"lib\": [\"es5\"] } }\n// @Filename: /home/src/workspaces/project/a/index.ts\nclass A {}\n// @Filename: /home/src/workspaces/project/a/index.d.ts\ndeclare class A {\n}\n//# sourceMappingURL=index.d.ts.map\n// @Filename: /home/src/workspaces/project/a/index.d.ts.map\n{\"version\":3,\"file\":\"index.d.ts\",\"sourceRoot\":\"\",\"sources\":[\"index.ts\"],\"names\":[],\"mappings\":\"AAAA,OAAO,OAAO,CAAC;CAAG\"}\n// @Filename: /home/src/workspaces/project/b/tsconfig.json\n{\n  \"compilerOptions\": { \"disableSourceOfProjectReferenceRedirect\": true, \"lib\": [\"es5\"] },\n  \"references\": [{ \"path\": \"../a\" }]\n}\n// @Filename: /home/src/workspaces/project/b/b.ts\n/// <reference path=\"../a/index.d.ts\" />\nnew A/**/();\n// @Filename: /home/src/workspaces/project/c/package.json\n{ \"dependencies\": { \"a\": \"*\" } }\n// @Filename: /home/src/workspaces/project/c/tsconfig.json\n{ \"compilerOptions\": { \"lib\": [\"es5\"] }, \"references\" [{ \"path\": \"../a\" }] }\n// @Filename: /home/src/workspaces/project/c/index.ts\nexport {};\n// @link: /home/src/workspaces/project/a -> /home/src/workspaces/project/c/node_modules/a";
            var (f, done) = F.Fourslash.NewFourslash(t, null, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.GoToFile(t, "/home/src/workspaces/project/c/index.ts");
                f.VerifyBaselineFindAllReferences(t, "");
            }
            finally
            {
                done();
            }
        });
    }

}
