// ported from: internal/fourslash/tests/quickInfoDisplayPartsClassMethodVS_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoDisplayPartsClassMethodVSCase
{
    public static void TestQuickInfoDisplayPartsClassMethodVS(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class c {\n    public /*1*/publicMethod() { }\n    private /*2*/privateMethod() { }\n    protected /*21*/protectedMethod() { }\n    static /*3*/staticMethod() { }\n    private static /*4*/privateStaticMethod() { }\n    protected static /*41*/protectedStaticMethod() { }\n    method() {\n        this./*5*/publicMethod();\n        this./*6*/privateMethod();\n        this./*61*/protectedMethod();\n        c./*7*/staticMethod();\n        c./*8*/privateStaticMethod();\n        c./*81*/protectedStaticMethod();\n    }\n}\nvar cInstance = new c();\n/*9*/cInstance./*10*/publicMethod();\n/*11*/c./*12*/staticMethod();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, new LSP.ClientCapabilities { VSSupportsVisualStudioExtensions = true }, content);
            try
            {
                f.VerifyBaselineVSHover(t);
            }
            finally
            {
                done();
            }
        });
    }

}
