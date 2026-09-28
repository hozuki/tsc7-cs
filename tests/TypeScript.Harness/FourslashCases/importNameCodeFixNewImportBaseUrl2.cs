// ported from: internal/fourslash/tests/importNameCodeFixNewImportBaseUrl2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using MS = TypeScript.ModuleSpecifiers;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFixNewImportBaseUrl2Case
{
    public static void TestImportNameCodeFixNewImportBaseUrl2(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /tsconfig.json\n{\n    \"compilerOptions\": {\n        \"baseUrl\": \"./a\"\n    }\n}\n// @Filename: /a/b/x.ts\nexport function f1() { };\n// @Filename: /a/c/y.ts\n[|f1/*0*/();|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/a/c/y.ts");
                f.VerifyImportFixAtPosition(t, ["import { f1 } from \"b/x\";\n\nf1();"], null!);
                f.VerifyImportFixAtPosition(t, ["import { f1 } from \"../b/x\";\n\nf1();"], new LSUP.UserPreferences { ImportModuleSpecifierPreference = (MS.ImportModuleSpecifierPreference)("relative") });
            }
            finally
            {
                done();
            }
        });
    }

}
