// ported from: internal/fourslash/tests/importNameCodeFixNewImportTypeRoots1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFixNewImportTypeRoots1Case
{
    public static void TestImportNameCodeFixNewImportTypeRoots1(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: a/f1.ts\n[|foo/*0*/();|]\n// @Filename: types/random/index.ts\nexport function foo() {};\n// @Filename: tsconfig.json\n{\n    \"compilerOptions\": {\n        \"baseUrl\": \".\",\n        \"typeRoots\": [\n            \"./types\"\n        ]\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixAtPosition(t, ["import { foo } from \"types/random\";\n\nfoo();"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
