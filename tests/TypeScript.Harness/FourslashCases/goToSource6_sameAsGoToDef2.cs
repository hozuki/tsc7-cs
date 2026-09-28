// ported from: internal/fourslash/tests/goToSource6_sameAsGoToDef2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToSource6_sameAsGoToDef2Case
{
    public static void TestGoToSource6_sameAsGoToDef2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n// @Filename: /home/src/workspaces/project/node_modules/foo/package.json\n{ \"name\": \"foo\", \"version\": \"1.2.3\", \"typesVersions\": { \"*\": { \"*\": [\"./types/*\"] } } }\n// @Filename: /home/src/workspaces/project/node_modules/foo/src/a.ts\nexport const /*end*/a = 'a';\n// @Filename: /home/src/workspaces/project/node_modules/foo/types/a.d.ts\nexport declare const a: string;\n//# sourceMappingURL=a.d.ts.map\n// @Filename: /home/src/workspaces/project/node_modules/foo/types/a.d.ts.map\n{\"version\":3,\"file\":\"a.d.ts\",\"sourceRoot\":\"\",\"sources\":[\"../src/a.ts\"],\"names\":[],\"mappings\":\"AAAA,eAAO,MAAM,EAAE,OAAO,CAAC;;AACvB,wBAAsB\"}\n// @Filename: /home/src/workspaces/project/node_modules/foo/dist/a.js\nexport const a = 'a';\n// @Filename: /home/src/workspaces/project/b.ts\nimport { a } from 'foo/a';\n[|a/*start*/|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.VerifyBaselineGoToSourceDefinition(t, "start");
                f.VerifyBaselineGoToDefinition(t, true, "start");
            }
            finally
            {
                done();
            }
        });
    }

}
