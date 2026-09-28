// ported from: internal/fourslash/tests/referenceToClass_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ReferenceToClassCase
{
    public static void TestReferenceToClass(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: referenceToClass_1.ts\nclass /*1*/foo {\n    public n: /*2*/foo;\n    public foo: number;\n}\n\nclass bar {\n    public n: /*3*/foo;\n    public k = new /*4*/foo();\n}\n\nnamespace mod {\n    var k: /*5*/foo = null;\n}\n// @Filename: referenceToClass_2.ts\nvar k: /*6*/foo;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1", "2", "3", "4", "5", "6");
            }
            finally
            {
                done();
            }
        });
    }

}
