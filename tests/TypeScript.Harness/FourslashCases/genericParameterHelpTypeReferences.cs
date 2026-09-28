// ported from: internal/fourslash/tests/genericParameterHelpTypeReferences_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GenericParameterHelpTypeReferencesCase
{
    public static void TestGenericParameterHelpTypeReferences(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface IFoo { }\n\nclass testClass<T extends IFoo, U, M extends IFoo> {\n    constructor(a:T, b:U, c:M){ }\n}\n\n// Generic types\ntestClass</*type1*/\nvar x : testClass</*type2*/\nclass Bar<T> extends testClass</*type3*/\nvar x : testClass<,, /*type4*/any>;\n\ninterface I<T> {}\nlet i: I</*interface*/>;\n\ntype Ty<T> = T;\nlet t: Ty</*typeAlias*/>;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "type1");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "testClass<T extends IFoo, U, M extends IFoo>", ParameterName = "T", ParameterSpan = "T extends IFoo" });
                f.GoToMarker(t, "type2");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "testClass<T extends IFoo, U, M extends IFoo>", ParameterName = "T", ParameterSpan = "T extends IFoo" });
                f.GoToMarker(t, "type3");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "testClass<T extends IFoo, U, M extends IFoo>", ParameterName = "T", ParameterSpan = "T extends IFoo" });
                f.GoToMarker(t, "type4");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { ParameterName = "M", ParameterSpan = "M extends IFoo" });
                f.GoToMarker(t, "interface");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "I<T>", ParameterName = "T", ParameterSpan = "T" });
                f.GoToMarker(t, "typeAlias");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "Ty<T>", ParameterName = "T", ParameterSpan = "T" });
            }
            finally
            {
                done();
            }
        });
    }

}
