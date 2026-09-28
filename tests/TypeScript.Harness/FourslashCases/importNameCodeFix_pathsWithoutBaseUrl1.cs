// ported from: internal/fourslash/tests/importNameCodeFix_pathsWithoutBaseUrl1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFix_pathsWithoutBaseUrl1Case
{
    public static void TestImportNameCodeFix_pathsWithoutBaseUrl1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: tsconfig.json\n{\n  \"compilerOptions\": {\n    \"module\": \"commonjs\",\n    \"paths\": {\n      \"@app/*\": [\"./lib/*\"]\n    }\n  }\n}\n// @Filename: index.ts\nutils/**/\n// @Filename: lib/utils.ts\nexport const utils = {};"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifyImportFixAtPosition(t, ["import { utils } from \"@app/utils\";\n\nutils"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
