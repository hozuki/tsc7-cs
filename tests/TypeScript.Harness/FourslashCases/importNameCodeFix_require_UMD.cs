// ported from: internal/fourslash/tests/importNameCodeFix_require_UMD_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFix_require_UMDCase
{
    public static void TestImportNameCodeFix_require_UMD(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @checkJs: true\n// @module: commonjs\n// @esModuleInterop: false\n// @allowSyntheticDefaultImports: false\n// @Filename: umd.d.ts\nnamespace Foo { function f() {} }\nexport = Foo;\nexport as namespace Foo;\n// @Filename: index.js\nFoo;\nmodule.exports = {};"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "index.js");
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Add import from \"./umd\"", NewFileContent = "const Foo = require(\"./umd\");\n\nFoo;\nmodule.exports = {};", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
