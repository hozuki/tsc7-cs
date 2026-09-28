// ported from: internal/fourslash/tests/importNameCodeFixNewImportExportEqualsCommonJSInteropOn_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFixNewImportExportEqualsCommonJSInteropOnCase
{
    public static void TestImportNameCodeFixNewImportExportEqualsCommonJSInteropOn(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Module: commonjs\n// @EsModuleInterop: true\n// @Filename: /foo.d.ts\ndeclare module \"bar\" {\n  const bar: number;\n  export = bar;\n}\ndeclare module \"foo\" {\n  const foo: number;\n  export = foo;\n}\ndeclare module \"es\" {\n  const es = 0;\n  export default es;\n}\n// @Filename: /a.ts\nimport bar = require(\"bar\");\n\nfoo\n// @Filename: /b.ts\nfoo\n// @Filename: /c.ts\nimport es from \"es\";\nimport bar = require(\"bar\");\n\nfoo"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/a.ts");
                f.VerifyImportFixAtPosition(t, ["import bar = require(\"bar\");\nimport foo = require(\"foo\");\n\nfoo"], null!);
                f.GoToFile(t, "/b.ts");
                f.VerifyImportFixAtPosition(t, ["import foo from \"foo\";\n\nfoo"], null!);
                f.GoToFile(t, "/c.ts");
                f.VerifyImportFixAtPosition(t, ["import es from \"es\";\nimport bar = require(\"bar\");\nimport foo = require(\"foo\");\n\nfoo"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
