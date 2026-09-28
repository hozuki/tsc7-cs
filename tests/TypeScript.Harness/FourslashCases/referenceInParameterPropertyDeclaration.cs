// ported from: internal/fourslash/tests/referenceInParameterPropertyDeclaration_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ReferenceInParameterPropertyDeclarationCase
{
    public static void TestReferenceInParameterPropertyDeclaration(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: file1.ts\nclass Foo {\n    constructor(private /*1*/privateParam: number,\n        public /*2*/publicParam: string,\n        protected /*3*/protectedParam: boolean) {\n\n        let localPrivate = privateParam;\n        this.privateParam += 10;\n\n        let localPublic = publicParam;\n        this.publicParam += \" Hello!\";\n\n        let localProtected = protectedParam;\n        this.protectedParam = false;\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1", "2", "3");
            }
            finally
            {
                done();
            }
        });
    }

}
