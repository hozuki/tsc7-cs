// ported from: internal/fourslash/tests/referencesForImports_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ReferencesForImportsCase
{
    public static void TestReferencesForImports(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("declare module \"jquery\" {\n    function $(s: string): any;\n    export = $;\n}\n/*1*/import /*2*/$ = require(\"jquery\");\n/*3*/$(\"a\");\n/*4*/import /*5*/$ = require(\"jquery\");"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1", "2", "3", "4", "5");
            }
            finally
            {
                done();
            }
        });
    }

}
