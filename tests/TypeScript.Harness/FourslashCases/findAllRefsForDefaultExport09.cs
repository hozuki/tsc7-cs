// ported from: internal/fourslash/tests/findAllRefsForDefaultExport09_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsForDefaultExport09Case
{
    public static void TestFindAllRefsForDefaultExport09(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @filename: /tsconfig.json\n{\n    \"compilerOptions\": {\n        \"target\": \"esnext\",\n        \"strict\": true,\n        \"outDir\": \"./out\",\n        \"allowSyntheticDefaultImports\": true\n    }\n}\n// @filename: /a.js\nmodule.exports = [];\n// @filename: /b.js\nmodule.exports = 1;\n// @filename: /c.ts\nexport = [];\n// @filename: /d.ts\nexport = 1;\n// @filename: /foo.ts\nimport * as /*0*/a from \"./a.js\"\nimport /*1*/aDefault from \"./a.js\"\nimport * as /*2*/b from \"./b.js\"\nimport /*3*/bDefault from \"./b.js\"\n\nimport * as /*4*/c from \"./c\"\nimport /*5*/cDefault from \"./c\"\nimport * as /*6*/d from \"./d\"\nimport /*7*/dDefault from \"./d\""u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "0", "1", "2", "3", "4", "5", "6", "7");
            }
            finally
            {
                done();
            }
        });
    }

}
