// ported from: internal/fourslash/tests/findAllRefsImportEqualsJsonFile_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsImportEqualsJsonFileCase
{
    public static void TestFindAllRefsImportEqualsJsonFile(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @checkJs: true\n// @resolveJsonModule: true\n// @module: commonjs\n// @Filename: /a.ts\nimport /*0*/j = require(\"/*1*/./j.json\");\n/*2*/j;\n// @Filename: /b.js\nconst /*3*/j = require(\"/*4*/./j.json\");\n/*5*/j;\n// @Filename: /j.json\n/*6*/{ \"x\": 0 }"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNoErrors(t);
                f.VerifyBaselineFindAllReferences(t, "0", "2", "1", "4", "3", "5", "6");
            }
            finally
            {
                done();
            }
        });
    }

}
