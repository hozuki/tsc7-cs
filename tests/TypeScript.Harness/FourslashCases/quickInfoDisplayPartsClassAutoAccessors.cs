// ported from: internal/fourslash/tests/quickInfoDisplayPartsClassAutoAccessors_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoDisplayPartsClassAutoAccessorsCase
{
    public static void TestQuickInfoDisplayPartsClassAutoAccessors(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class c {\n    public accessor /*1a*/publicProperty: string;\n    private accessor /*2a*/privateProperty: string;\n    protected accessor /*3a*/protectedProperty: string;\n    static accessor /*4a*/staticProperty: string;\n    private static accessor /*5a*/privateStaticProperty: string;\n    protected static accessor /*6a*/protectedStaticProperty: string;\n    method() {\n        var x: string;\n        x = this./*1g*/publicProperty;\n        x = this./*2g*/privateProperty;\n        x = this./*3g*/protectedProperty;\n        x = c./*4g*/staticProperty;\n        x = c./*5g*/privateStaticProperty;\n        x = c./*6g*/protectedStaticProperty;\n        this./*1s*/publicProperty = \"\";\n        this./*2s*/privateProperty = \"\";\n        this./*3s*/protectedProperty = \"\";\n        c./*4s*/staticProperty = \"\";\n        c./*5s*/privateStaticProperty = \"\";\n        c./*6s*/protectedStaticProperty = \"\";\n    }\n}\nvar cInstance = new c();\nvar y: string;\ny = /*7g*/cInstance./*8g*/publicProperty;\ny = /*9g*/c./*10g*/staticProperty;\n/*7s*/cInstance./*8s*/publicProperty = y;\n/*9s*/c./*10s*/staticProperty = y;"u8);
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
