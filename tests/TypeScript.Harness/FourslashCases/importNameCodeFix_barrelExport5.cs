// ported from: internal/fourslash/tests/importNameCodeFix_barrelExport5_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFix_barrelExport5Case
{
    public static void TestImportNameCodeFix_barrelExport5(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: node18\n// @Filename: /package.json\n{ \"type\": \"module\" }\n// @Filename: /foo/a.ts\nexport const A = 0;\n// @Filename: /foo/b.ts\nexport {};\nA/*sibling*/\n// @Filename: /foo/index.ts\nexport * from \"./a.js\";\nexport * from \"./b.js\";\n// @Filename: /index.ts\nexport * from \"./foo/index.js\";\nexport * from \"./src/index.js\";\n// @Filename: /src/a.ts\nexport {};\nA/*parent*/\n// @Filename: /src/index.ts\nexport * from \"./a.js\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixModuleSpecifiers(t, "sibling", ["./a.js", "./index.js", "../index.js"], null!);
                f.VerifyImportFixModuleSpecifiers(t, "parent", ["../foo/a.js", "../foo/index.js", "../index.js"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
