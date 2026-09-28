// ported from: internal/fourslash/tests/findAllRefsThisKeywordMultipleFiles_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsThisKeywordMultipleFilesCase
{
    public static void TestFindAllRefsThisKeywordMultipleFiles(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: file1.ts\n/*1*/this; /*2*/this;\n// @Filename: file2.ts\n/*3*/this;\n/*4*/this;\n// @Filename: file3.ts\n ((x = /*5*/this, y) => /*6*/this)(/*7*/this, /*8*/this);\n // different 'this'\n function f(this) { return this; }"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1", "2", "3", "4", "5", "6", "7", "8");
            }
            finally
            {
                done();
            }
        });
    }

}
