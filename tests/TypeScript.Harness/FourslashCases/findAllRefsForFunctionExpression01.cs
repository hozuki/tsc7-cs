// ported from: internal/fourslash/tests/findAllRefsForFunctionExpression01_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsForFunctionExpression01Case
{
    public static void TestFindAllRefsForFunctionExpression01(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: file1.ts\nvar foo = /*1*/function /*2*/foo(a = /*3*/foo(), b = () => /*4*/foo) {\n    /*5*/foo(/*6*/foo, /*7*/foo);\n}\n// @Filename: file2.ts\n/// <reference path=\"file1.ts\" />\nfoo();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1", "2", "3", "4", "5", "6", "7");
            }
            finally
            {
                done();
            }
        });
    }

}
