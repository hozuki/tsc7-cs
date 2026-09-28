// ported from: internal/fourslash/tests/findAllRefsNonModule_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsNonModuleCase
{
    public static void TestFindAllRefsNonModule(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @checkJs: true\n// @Filename: /script.ts\nconsole.log(\"I'm a script!\");\n// @Filename: /import.ts\nimport \"./script/*1*/\";\n// @Filename: /require.js\nrequire(\"./script/*2*/\");\nconsole.log(\"./script/*3*/\");\n// @Filename: /tripleSlash.ts\n/// <reference path=\"script.ts\" />\n// @Filename: /stringLiteral.ts\nconsole.log(\"./script\");"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1", "2", "3");
            }
            finally
            {
                done();
            }
        });
    }

}
