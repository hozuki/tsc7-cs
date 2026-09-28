// ported from: internal/fourslash/tests/quickInfoDisplayPartsClassPropertyVS_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoDisplayPartsClassPropertyVSCase
{
    public static void TestQuickInfoDisplayPartsClassPropertyVS(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class c {\n    public /*1*/publicProperty: string;\n    private /*2*/privateProperty: string;\n    protected /*21*/protectedProperty: string;\n    static /*3*/staticProperty: string;\n    private static /*4*/privateStaticProperty: string;\n    protected static /*41*/protectedStaticProperty: string;\n    method() {\n        this./*5*/publicProperty;\n        this./*6*/privateProperty;\n        this./*61*/protectedProperty;\n        c./*7*/staticProperty;\n        c./*8*/privateStaticProperty;\n        c./*81*/protectedStaticProperty;\n    }\n}\nvar cInstance = new c();\n/*9*/cInstance./*10*/publicProperty;\n/*11*/c./*12*/staticProperty;"u8);
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
