// ported from: internal/fourslash/tests/referencesForMergedDeclarations3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ReferencesForMergedDeclarations3Case
{
    public static void TestReferencesForMergedDeclarations3(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("[|class /*class*/[|testClass|] {\n    static staticMethod() { }\n    method() { }\n}|]\n\n[|module /*module*/[|testClass|] {\n    export interface Bar {\n\n    }\n}|]\n\nvar c1: [|testClass|];\nvar c2: [|testClass|].Bar;\n[|testClass|].staticMethod();\n[|testClass|].prototype.method();\n[|testClass|].bind(this);\nnew [|testClass|]();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "module", "class");
            }
            finally
            {
                done();
            }
        });
    }

}
