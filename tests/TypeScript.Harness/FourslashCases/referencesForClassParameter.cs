// ported from: internal/fourslash/tests/referencesForClassParameter_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ReferencesForClassParameterCase
{
    public static void TestReferencesForClassParameter(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("var p = 2;\n\nclass p { }\n\nclass foo {\n    constructor (/*1*/public /*2*/p: any) {\n    }\n\n    public f(p) {\n        this./*3*/p = p;\n    }\n\n}\n\nvar n = new foo(undefined);\nn./*4*/p = null;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1", "2", "3", "4");
            }
            finally
            {
                done();
            }
        });
    }

}
