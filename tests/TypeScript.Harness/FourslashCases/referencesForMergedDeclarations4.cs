// ported from: internal/fourslash/tests/referencesForMergedDeclarations4_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ReferencesForMergedDeclarations4Case
{
    public static void TestReferencesForMergedDeclarations4(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/*1*/class /*2*/testClass {\n    static staticMethod() { }\n    method() { }\n}\n\n/*3*/module /*4*/testClass {\n    export interface Bar {\n\n    }\n    export var s = 0;\n}\n\nvar c1: /*5*/testClass;\nvar c2: /*6*/testClass.Bar;\n/*7*/testClass.staticMethod();\n/*8*/testClass.prototype.method();\n/*9*/testClass.bind(this);\n/*10*/testClass.s;\nnew /*11*/testClass();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11");
            }
            finally
            {
                done();
            }
        });
    }

}
