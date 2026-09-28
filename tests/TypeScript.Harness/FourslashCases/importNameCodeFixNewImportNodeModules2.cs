// ported from: internal/fourslash/tests/importNameCodeFixNewImportNodeModules2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFixNewImportNodeModules2Case
{
    public static void TestImportNameCodeFixNewImportNodeModules2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("[|f1/*0*/();|]\n// @Filename: ../package.json\n{ \"dependencies\": { \"fake-module\": \"latest\" } }\n// @Filename: ../node_modules/fake-module/notindex.d.ts\nexport var v1 = 5;\nexport function f1();\n// @Filename: ../node_modules/fake-module/notindex.js\nmodule.exports = {\n   v1: 5,\n   f1: function () {}\n};\n// @Filename: ../node_modules/fake-module/package.json\n{ \"main\":\"./notindex.js\", \"typings\":\"./notindex.d.ts\" }"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixAtPosition(t, ["import { f1 } from \"fake-module\";\n\nf1();"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
