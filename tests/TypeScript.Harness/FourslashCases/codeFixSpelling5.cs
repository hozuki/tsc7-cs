// ported from: internal/fourslash/tests/codeFixSpelling5_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixSpelling5Case
{
    public static void TestCodeFixSpelling5(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: f1.ts\nexport const fooooooooo = 1;\n// @Filename: f2.ts\nimport {[|fooooooooa|]} from \"./f1\"; fooooooooa;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "f2.ts");
                f.VerifyRangeAfterCodeFix(t, "fooooooooo", false, 0, 0);
            }
            finally
            {
                done();
            }
        });
    }

}
