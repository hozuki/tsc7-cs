// ported from: internal/fourslash/tests/findAllRefsImportDefault_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsImportDefaultCase
{
    public static void TestFindAllRefsImportDefault(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: f.ts\nexport { foo as default };\nfunction /*start*/foo(a: number, b: number) {\n    return a + b;\n}\n// @Filename: b.ts\nimport bar from \"./f\";\nbar(1, 2);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNoErrors(t);
                f.VerifyBaselineFindAllReferences(t, "start");
            }
            finally
            {
                done();
            }
        });
    }

}
