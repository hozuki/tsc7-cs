// ported from: internal/fourslash/tests/incrementalResolveFunctionPropertyAssignment_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class IncrementalResolveFunctionPropertyAssignmentCase
{
    public static void TestIncrementalResolveFunctionPropertyAssignment(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function bar(indexer: { getLength(): number; getTypeAtIndex(index: number): string; }): string {\n    return indexer.getTypeAtIndex(indexer.getLength() - 1);\n}\nfunction foo(a: string[]) {\n    return bar({\n        getLength(): number {\n            return \"a.length\";\n        },\n        getTypeAtIndex(index: number) {\n            switch (index) {\n                case 0: return a[0];\n                case 1: return a[1];\n                case 2: return a[2];\n                default: return \"invalid\";\n            }\n        }\n    });\n}\nvar val = foo([\"myString1\", \"myString2\"]);\n/*1*/val;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "var val: string", "");
                f.VerifyNumberOfErrorsInCurrentFile(t, 1);
            }
            finally
            {
                done();
            }
        });
    }

}
