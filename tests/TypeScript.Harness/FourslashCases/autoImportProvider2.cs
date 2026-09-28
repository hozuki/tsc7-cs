// ported from: internal/fourslash/tests/autoImportProvider2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportProvider2Case
{
    public static void TestAutoImportProvider2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /home/src/workspaces/project/node_modules/direct-dependency/package.json\n{ \"name\": \"direct-dependency\", \"dependencies\": { \"indirect-dependency\": \"*\" } }\n// @Filename: /home/src/workspaces/project/node_modules/direct-dependency/index.d.ts\nimport \"indirect-dependency\";\nexport declare class DirectDependency {}\n// @Filename: /home/src/workspaces/project/node_modules/indirect-dependency/package.json\n{ \"name\": \"indirect-dependency\" }\n// @Filename: /home/src/workspaces/project/node_modules/indirect-dependency/index.d.ts\nexport declare class IndirectDependency\n// @Filename: /home/src/workspaces/project/tsconfig.json\n{ \"compilerOptions\": { \"lib\": [\"es5\"] } }\n// @Filename: /home/src/workspaces/project/package.json\n{ \"dependencies\": { \"direct-dependency\": \"*\" } }\n// @Filename: /home/src/workspaces/project/index.ts\nIndirectDependency/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.GoToMarker(t, "");
                var opts1155 = f.GetOptions();
                opts1155!.FormatCodeSettings!.NewLineCharacter = "\n";
                f.Configure(t, opts1155);
                f.VerifyImportFixAtPosition(t, [], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
