// ported from: internal/fourslash/tests/addAllMissingImportsNoCrash_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AddAllMissingImportsNoCrashCase
{
    public static void TestAddAllMissingImportsNoCrash(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: file1.ts\nexport interface Test1 {}\nexport interface Test2 {}\nexport interface Test3 {}\nexport interface Test4 {}\n// @Filename: file2.ts\nimport { Test1, Test4 } from './file1';\ninterface Testing {\n    test1: Test1;\n    test2: Test2;\n    test3: Test3;\n    test4: Test4;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "file2.ts");
                f.VerifyCodeFixAll(t, new F.VerifyCodeFixAllOptions { FixID = "fixMissingImport", NewFileContent = "import { Test1, Test2, Test3, Test4 } from './file1';\ninterface Testing {\n    test1: Test1;\n    test2: Test2;\n    test3: Test3;\n    test4: Test4;\n}" });
            }
            finally
            {
                done();
            }
        });
    }

}
