// ported from: internal/fourslash/tests/importNameCodeFix_externalNonRelative1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using MS = TypeScript.ModuleSpecifiers;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFix_externalNonRelative1Case
{
    public static void TestImportNameCodeFix_externalNonRelative1(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /home/src/workspaces/project/tsconfig.base.json\n{\n  \"compilerOptions\": {\n    \"module\": \"commonjs\",\n    \"lib\": [\"es5\"],\n    \"paths\": {\n      \"pkg-1/*\": [\"./packages/pkg-1/src/*\"],\n      \"pkg-2/*\": [\"./packages/pkg-2/src/*\"]\n    }\n  }\n}\n// @Filename: /home/src/workspaces/project/packages/pkg-1/package.json\n{ \"dependencies\": { \"pkg-2\": \"*\" } }\n// @Filename: /home/src/workspaces/project/packages/pkg-1/tsconfig.json\n{\n  \"extends\": \"../../tsconfig.base.json\",\n  \"references\": [\n    { \"path\": \"../pkg-2\" }\n  ]\n}\n// @Filename: /home/src/workspaces/project/packages/pkg-1/src/index.ts\nPkg2/*external*/\n// @Filename: /home/src/workspaces/project/packages/pkg-2/package.json\n{ \"types\": \"dist/index.d.ts\" }\n// @Filename: /home/src/workspaces/project/packages/pkg-2/tsconfig.json\n{\n  \"extends\": \"../../tsconfig.base.json\",\n  \"compilerOptions\": { \"outDir\": \"dist\", \"rootDir\": \"src\", \"composite\": true, \"lib\": [\"es5\"] }\n}\n// @Filename: /home/src/workspaces/project/packages/pkg-2/src/index.ts\nimport \"./utils\";\n// @Filename: /home/src/workspaces/project/packages/pkg-2/src/utils.ts\nexport const Pkg2 = {};\n// @Filename: /home/src/workspaces/project/packages/pkg-2/src/blah/foo/data.ts\nPkg2/*internal*/\n// @link: /home/src/workspaces/project/packages/pkg-2 -> /home/src/workspaces/project/packages/pkg-1/node_modules/pkg-2"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                var opts1534 = f.GetOptions();
                opts1534!.FormatCodeSettings!.NewLineCharacter = "\n";
                f.Configure(t, opts1534);
                f.GoToMarker(t, "external");
                f.VerifyImportFixAtPosition(t, ["import { Pkg2 } from \"pkg-2/utils\";\n\nPkg2"], new LSUP.UserPreferences { ImportModuleSpecifierPreference = (MS.ImportModuleSpecifierPreference)("project-relative") });
                f.GoToMarker(t, "internal");
                f.VerifyImportFixAtPosition(t, ["import { Pkg2 } from \"../../utils\";\n\nPkg2"], new LSUP.UserPreferences { ImportModuleSpecifierPreference = (MS.ImportModuleSpecifierPreference)("project-relative") });
            }
            finally
            {
                done();
            }
        });
    }

}
