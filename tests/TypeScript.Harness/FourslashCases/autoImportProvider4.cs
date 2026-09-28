// ported from: internal/fourslash/tests/autoImportProvider4_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportProvider4Case
{
    public static void TestAutoImportProvider4(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /home/src/workspaces/project/a/package.json\n{ \"dependencies\": { \"b\": \"*\" } }\n// @Filename: /home/src/workspaces/project/a/tsconfig.json\n{ \"compilerOptions\": { \"lib\": [\"es5\"], \"module\": \"commonjs\", \"target\": \"esnext\" }, \"references\": [{ \"path\": \"../b\" }] }\n// @Filename: /home/src/workspaces/project/a/index.ts\nnew Shape/**/\n// @Filename: /home/src/workspaces/project/b/package.json\n{ \"types\": \"out/index.d.ts\" }\n// @Filename: /home/src/workspaces/project/b/tsconfig.json\n{ \"compilerOptions\": { \"lib\": [\"es5\"], \"outDir\": \"out\", \"composite\": true } }\n// @Filename: /home/src/workspaces/project/b/index.ts\nexport class Shape {}\n// @link: /home/src/workspaces/project/b -> /home/src/workspaces/project/a/node_modules/b"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.GoToMarker(t, "");
                f.VerifyImportFixAtPosition(t, ["import { Shape } from \"b\";\n\nnew Shape"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
