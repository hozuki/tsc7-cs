// ported from: internal/fourslash/tests/importHelpersAfterScriptBecomesDecoratedModule_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportHelpersAfterScriptBecomesDecoratedModuleCase
{
    public static void TestImportHelpersAfterScriptBecomesDecoratedModule(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /tsconfig.json\n{\n\t\"compilerOptions\": {\n\t\t\"target\": \"es2015\",\n\t\t\"module\": \"commonjs\",\n\t\t\"experimentalDecorators\": true,\n\t\t\"importHelpers\": true\n\t},\n\t\"files\": [\"foo.ts\"]\n}\n\n// @Filename: /foo.ts\ndeclare function dec(value: Function): void;\n/*insert*/class C {}\n\n// @Filename: /node_modules/tslib/package.json\n{ \"name\": \"tslib\", \"typings\": \"tslib.d.ts\" }\n\n// @Filename: /node_modules/tslib/tslib.d.ts\nexport declare function __decorate(...args: any[]): any;\n\n// @Filename: /node_modules/tslib/tslib.js\nexports.__decorate = function () {};\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/foo.ts");
                f.VerifyNumberOfErrorsInCurrentFile(t, 0);
                f.Replace(t, f.MarkerByName(t, "insert")!.Position, 0, "@dec\nexport ");
                f.VerifyNumberOfErrorsInCurrentFile(t, 0);
            }
            finally
            {
                done();
            }
        });
    }

}
