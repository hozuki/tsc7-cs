// ported from: internal/fourslash/tests/quickInfoDisplayPartsClassAccessors_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoDisplayPartsClassAccessorsCase
{
    public static void TestQuickInfoDisplayPartsClassAccessors(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class c {\n    public get /*1*/publicProperty() { return \"\"; }\n    public set /*1s*/publicProperty(x: string) { }\n    private get /*2*/privateProperty() { return \"\"; }\n    private set /*2s*/privateProperty(x: string) { }\n    protected get /*21*/protectedProperty() { return \"\"; }\n    protected set /*21s*/protectedProperty(x: string) { }\n    static get /*3*/staticProperty() { return \"\"; }\n    static set /*3s*/staticProperty(x: string) { }\n    private static get  /*4*/privateStaticProperty() { return \"\"; }\n    private static set /*4s*/privateStaticProperty(x: string) { }\n    protected static get /*41*/protectedStaticProperty() { return \"\"; }\n    protected static set /*41s*/protectedStaticProperty(x: string) { }\n    method() {\n        var x : string;\n        x = this./*5*/publicProperty;\n        x = this./*6*/privateProperty;\n        x = this./*61*/protectedProperty;\n        x = c./*7*/staticProperty;\n        x = c./*8*/privateStaticProperty;\n        x = c./*81*/protectedStaticProperty;\n        this./*5s*/publicProperty = \"\";\n        this./*6s*/privateProperty = \"\";\n        this./*61s*/protectedProperty = \"\";\n        c./*7s*/staticProperty = \"\";\n        c./*8s*/privateStaticProperty = \"\";\n        c./*81s*/protectedStaticProperty = \"\";\n    }\n}\nvar cInstance = new c();\nvar y: string;\ny = /*9*/cInstance./*10*/publicProperty;\ny = /*11*/c./*12*/staticProperty;\n/*9s*/cInstance./*10s*/publicProperty = y;\n/*11s*/c./*12s*/staticProperty = y;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHover(t);
            }
            finally
            {
                done();
            }
        });
    }

}
