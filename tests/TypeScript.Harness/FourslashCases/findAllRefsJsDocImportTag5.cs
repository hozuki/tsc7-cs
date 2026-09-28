// ported from: internal/fourslash/tests/findAllRefsJsDocImportTag5_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsJsDocImportTag5Case
{
    public static void TestFindAllRefsJsDocImportTag5(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @checkJs: true\n// @Filename: /a.js\nexport default function /*0*/a() {}\n// @Filename: /b.js\n/** @import /*1*/a, * as ns from \"./a\" */"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "0", "1");
            }
            finally
            {
                done();
            }
        });
    }

}
