// ported from: internal/fourslash/tests/autoImportProvider1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportProvider1Case
{
    public static void TestAutoImportProvider1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /home/src/workspaces/project/node_modules/@angular/forms/package.json\n{ \"name\": \"@angular/forms\", \"typings\": \"./forms.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/@angular/forms/forms.d.ts\nexport class PatternValidator {}\n// @Filename: /home/src/workspaces/project/tsconfig.json\n{ \"compilerOptions\": { \"lib\": [\"es5\"] } }\n// @Filename: /home/src/workspaces/project/package.json\n{ \"dependencies\": { \"@angular/forms\": \"*\" } }\n// @Filename: /home/src/workspaces/project/index.ts\nPatternValidator/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.GoToMarker(t, "");
                var opts654 = f.GetOptions();
                opts654!.FormatCodeSettings!.NewLineCharacter = "\n";
                f.Configure(t, opts654);
                f.VerifyImportFixAtPosition(t, ["import { PatternValidator } from \"@angular/forms\";\n\nPatternValidator"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
