// ported from: internal/fourslash/tests/findAllReferencesTripleSlash_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllReferencesTripleSlashCase
{
    public static void TestFindAllReferencesTripleSlash(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @checkJs: true\n// @Filename: /node_modules/@types/globals/index.d.ts\ndeclare const someAmbientGlobal: unknown;\n// @Filename: /a.ts\n/// <reference path=\"b.ts/*1*/\" />\n/// <reference types=\"globals/*2*/\" />\n// @Filename: /b.ts\nconsole.log(\"b.ts\");\n// @Filename: /c.js\nrequire(\"./b\");\nrequire(\"globals\");"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1", "2");
            }
            finally
            {
                done();
            }
        });
    }

}
