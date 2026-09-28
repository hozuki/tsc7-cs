// ported from: internal/fourslash/tests/moduleNodeNextAutoImport2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ModuleNodeNextAutoImport2Case
{
    public static void TestModuleNodeNextAutoImport2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /tsconfig.json\n{ \"compilerOptions\": { \"module\": \"nodenext\" } }\n// @Filename: /package.json\n{ \"type\": \"module\" }\n// @Filename: /mobx.d.cts\nexport declare function autorun(): void;\n// @Filename: /index.ts\nautorun/**/\n// @Filename: /utils.ts\nimport \"./mobx.cjs\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifyImportFixAtPosition(t, ["import { autorun } from \"./mobx.cjs\";\n\nautorun"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
