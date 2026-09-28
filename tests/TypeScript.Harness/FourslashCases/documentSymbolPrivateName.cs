// ported from: internal/fourslash/tests/documentSymbolPrivateName_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class DocumentSymbolPrivateNameCase
{
    public static void TestDocumentSymbolPrivateName(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: first.ts\nclass A {\n  #foo() {\n    class B {\n      #bar() {   \n         function baz () {\n         }\n      }\n    }\n  }\n}\n\nclass B {\n\tconstructor(private prop: string) {}\n}\n\n// @Filename: second.ts\nclass Foo {\n\t#privateProp: string;\n}\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineDocumentSymbol(t);
                f.GoToFile(t, "second.ts");
                f.VerifyBaselineDocumentSymbol(t);
            }
            finally
            {
                done();
            }
        });
    }

}
