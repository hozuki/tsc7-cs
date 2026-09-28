// ported from: internal/fourslash/tests/autoImportProvider9_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportProvider9Case
{
    public static void TestAutoImportProvider9(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n// @module: preserve\n// @Filename: /home/src/workspaces/project/index.ts\nLib1/**/\n// @Filename: /home/src/workspaces/project/package.json\n{\n  \"dependencies\": {\n    \"lib1\": \"*\",\n    \"lib2\": \"*\",\n    \"lib3\": \"*\",\n    \"lib4\": \"*\",\n    \"lib5\": \"*\",\n    \"lib6\": \"*\",\n    \"lib7\": \"*\",\n    \"lib8\": \"*\",\n    \"lib9\": \"*\",\n    \"lib10\": \"*\",\n    \"lib11\": \"*\"\n  }\n}\n// @Filename: /home/src/workspaces/project/node_modules/lib1/package.json\n{ \"name\": \"lib1\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/lib1/index.d.ts\nexport class Lib1 {}\n// @Filename: /home/src/workspaces/project/node_modules/lib2/package.json\n{ \"name\": \"lib2\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/lib2/index.d.ts\nexport class Lib2 {}\n// @Filename: /home/src/workspaces/project/node_modules/lib3/package.json\n{ \"name\": \"lib3\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/lib3/index.d.ts\nexport class Lib3 {}\n// @Filename: /home/src/workspaces/project/node_modules/lib4/package.json\n{ \"name\": \"lib4\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/lib4/index.d.ts\nexport class Lib4 {}\n// @Filename: /home/src/workspaces/project/node_modules/lib5/package.json\n{ \"name\": \"lib5\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/lib5/index.d.ts\nexport class Lib5 {}\n// @Filename: /home/src/workspaces/project/node_modules/lib6/package.json\n{ \"name\": \"lib6\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/lib6/index.d.ts\nexport class Lib6 {}\n// @Filename: /home/src/workspaces/project/node_modules/lib7/package.json\n{ \"name\": \"lib7\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/lib7/index.d.ts\nexport class Lib7 {}\n// @Filename: /home/src/workspaces/project/node_modules/lib8/package.json\n{ \"name\": \"lib8\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/lib8/index.d.ts\nexport class Lib8 {}\n// @Filename: /home/src/workspaces/project/node_modules/lib9/package.json\n{ \"name\": \"lib9\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/lib9/index.d.ts\nexport class Lib9 {}\n// @Filename: /home/src/workspaces/project/node_modules/lib10/package.json\n{ \"name\": \"lib10\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/lib10/index.d.ts\nexport class Lib10 {}\n// @Filename: /home/src/workspaces/project/node_modules/lib11/package.json\n{ \"name\": \"lib11\", \"types\": \"./index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/lib11/index.d.ts\nexport class Lib11 {}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.VerifyImportFixModuleSpecifiers(t, "", [], null!);
                f.VerifyImportFixModuleSpecifiers(t, "", [], null!);
                f.InsertLine(t, "import {} from 'lib2';");
                f.VerifyImportFixModuleSpecifiers(t, "", ["lib1"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
