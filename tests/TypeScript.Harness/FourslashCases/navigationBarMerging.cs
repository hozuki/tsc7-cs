// ported from: internal/fourslash/tests/navigationBarMerging_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class NavigationBarMergingCase
{
    public static void TestNavigationBarMerging(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: file1.ts\nnamespace a {\n    function foo() {}\n}\nnamespace b {\n    function foo() {}\n}\nnamespace a {\n    function bar() {}\n}\n// @Filename: file2.ts\nnamespace a {}\nfunction a() {}\n// @Filename: file3.ts\nnamespace a {\n    interface A {\n        foo: number;\n    }\n}\nnamespace a {\n    interface A {\n        bar: number;\n    }\n}\n// @Filename: file4.ts\nnamespace A { export var x; }\nnamespace A.B { export var y; }"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineDocumentSymbol(t);
                f.GoToFile(t, "file2.ts");
                f.VerifyBaselineDocumentSymbol(t);
                f.GoToFile(t, "file3.ts");
                f.VerifyBaselineDocumentSymbol(t);
                f.GoToFile(t, "file4.ts");
                f.VerifyBaselineDocumentSymbol(t);
            }
            finally
            {
                done();
            }
        });
    }

}
