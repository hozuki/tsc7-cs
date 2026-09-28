// ported from: internal/fourslash/tests/documentHighlightAtParameterPropertyDeclaration2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class DocumentHighlightAtParameterPropertyDeclaration2Case
{
    public static void TestDocumentHighlightAtParameterPropertyDeclaration2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: file1.ts\nclass Foo {\n    // This is not valid syntax: parameter property can't be binding pattern\n    constructor(private {[|privateParam|]}: number,\n        public {[|publicParam|]}: string,\n        protected {[|protectedParam|]}: boolean) {\n\n        let localPrivate = [|privateParam|];\n        this.privateParam += 10;\n\n        let localPublic = [|publicParam|];\n        this.publicParam += \" Hello!\";\n\n        let localProtected = [|protectedParam|];\n        this.protectedParam = false;\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineDocumentHighlights(t, null!, U.ToAny(f.Ranges()));
            }
            finally
            {
                done();
            }
        });
    }

}
