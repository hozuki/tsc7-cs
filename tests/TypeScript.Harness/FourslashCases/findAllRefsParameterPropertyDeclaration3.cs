// ported from: internal/fourslash/tests/findAllRefsParameterPropertyDeclaration3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsParameterPropertyDeclaration3Case
{
    public static void TestFindAllRefsParameterPropertyDeclaration3(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class Foo {\n    constructor(protected /*0*/protectedParam: number) {\n        let localProtected = /*1*/protectedParam;\n        this./*2*/protectedParam += 10;\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "0", "1", "2");
            }
            finally
            {
                done();
            }
        });
    }

}
